# SurfaceNets Terrain Generator

  Generator terenu 3D dla Unity wykorzystujący algorytm SurfaceNets do proceduralnego generowania gładkich mesh'y z danych voxelowych.

  ## Opis

  Projekt implementuje algorytm SurfaceNets do generowania terenu w czasie rzeczywistym. W przeciwieństwie do klasycznego Marching Cubes, SurfaceNets
  tworzy gładsze siatki z mniejszą liczbą artefaktów, co czyni go idealnym do generowania naturalnie wyglądających krajobrazów i ciał niebieskich.

  ## Główne funkcje

  - **Algorytm SurfaceNets** - generowanie gładkich mesh'y z danych voxelowych poprzez interpolację przecięć powierzchni
  - **Unity Job System + Burst** - wielowątkowe generowanie chunków dla maksymalnej wydajności
  - **System chunków** - dynamiczne ładowanie i wyładowywanie terenu wokół gracza
  - **Simplex Noise** - proceduralne generowanie gęstości terenu z warstwami szumu FractalNoise
  - **Generator ciał niebieskich** - wsparcie dla 6 typów obiektów:
    - Meteoroid (~8m) - małe skały
    - Small Asteroid (~25m) - średnie asteroidy
    - Asteroid (~100m) - duże asteroidy
    - Planetoid (~500m) - małe planety
    - Moon (~6000m) - księżyce
    - Comet (~15m) - komety częściowo pokryte lodem
  - **Optymalizacje**:
    - Przełączany culling pustych i pełnych chunków
    - Render distance jako kula nie sześcian
    - System LOD (3 poziomy szczegółowości) (W trakcie)
    - Dynamiczne zwalnianie odległych chunków
    - Limit ilości generowanych chunków jednosześnie

  ## Architektura
  
  ### Przepływ danych

  1. Player Position
  2. TerrainGenerator - sprawdza chunki w render distance
  3. Dodaje do kolejki generowania
  4. DensityJob (IJobParallelFor, batch 64) → NativeArray<Point> z wartościami gęstości
  5. CheckIsSurface() - culling pustych/pełnych chunków
  6. MeshJob (IJob) - algorytm SurfaceNets → NativeList<float3> vertices → NativeList<int> triangles
  7. ChunkSN.SetMesh() → Konwersja NativeArray → Unity Mesh
     
  ### Komponenty systemu

  #### *TerrainGenerator* - Główny kontroler całego systemu
  - Zarządza dynamicznym ładowaniem chunków wokół gracza
  - Kolejkuje chunki do generowania z limitem współbieżności
  - Optymalizuje pamięć poprzez usuwanie pustych/pełnych chunków
  - Przechowuje słowniki: załadowane chunki, zmodyfikowane chunki, zniszczone chunki

  #### *ChunkSN* - Pojedynczy chunk terenu (16³ voxeli)
  - Stałe rozmiary: Size=16, VoxelArraySize=17, DensityArraySize=18
  - Inicjalizuje mesh (vertices, triangles) z jobów i komponenty Unity 

  #### *CelestialBodyGenerator* - generowanie density dla różnych typów ciał niebieskich
  - Meteoroid (~8m) - prosty kształt, minimalne detale
  - SmallAsteroid (~25m) - 3 oktawy noise
  - Asteroid (~100m) - 6 oktaw, bardziej złożony
  - Planetoid (~500m) - płynne powierzchnie
  - Moon (~6000m) - kontynenty, góry, systemy tektoniczne
  - Comet (~15m) - niesymetryczny rdzeń lodowy
  - Używa Fractal Brownian Motion (FBM) z konfigurowalnymi parametrami
  - Oblicza dystans od środka + proceduralne zniekształcenia
  
  ### Podstawowa konfiguracja
  
  1. Dodaj prefab TerrainGenerator na scene
  2. Skonfiguruj TerrainGenerator:
     - Seed dla generatora
     - Typ ciała (Meteoroid, Asteroid, Moon, etc.)
     - Max Concurrent Gen - ile chunkow się jednocześnie max generuje
     - Destroy Air - czy omijać chunki które są całe powietrzem
     - Destroy Solid - czy omijać chunki pod ziemią
  4. Stwórz dowolny obiekt i przypisz jako Player
  5. Uruchom scenę - teren będzie generowany wokół gracza
  
  ### Parametry generatora
  
  Wydajność

  - Generowanie chunka: **~14ms** (w edytorze z włączonymi Destroy Air i Destroy Solid oraz MaxConcurrentGen: 1)
  - Mesh Job (IJob): single-threaded (wymaga sekwencyjnego dostępu)
  - Culling: pomija ~70% chunków przy typowej konfiguracji

  Technologia

  - C# Job System - wielowątkowe przetwarzanie
  - Burst Compiler - kompilacja do natywnego kodu maszynowego
  - NativeCollections - zero-copy transfer między jobami
  - Simplex Noise - gładki, proceduralny szum bez artefaktów
  - SurfaceNets Algorithm - dual contouring dla voxeli

  Znane ograniczenia

  - Brak systemu modyfikacji terenu w runtime (mining) - w trakcie
  - LOD z 3 stopniami szczegółowości (1, 0.5, 0.25) - w trakcie
  - liczenie normali za pomocą RecalculateBounds() tworzy widoczne szwy pomiędzy chunkami
  - Brak wsparcia dla tekstur
  - Mesh bez siatki UV

