# SurfaceNets Terrain Generator 
## WIP

<img width="1261" height="972" alt="image" src="https://github.com/user-attachments/assets/ab40477f-b23f-4721-b453-9447848562ca" />

  Generator terenu 3D dla Unity wykorzystujący algorytm SurfaceNets do proceduralnego generowania gładkich mesh'y z danych voxelowych.

  ## Opis
  
  Projekt implementuje algorytm SurfaceNets do generowania terenu w czasie rzeczywistym. 
  W przeciwieństwie do klasycznego Marching Cubes, SurfaceNets tworzy gładsze siatki z
  mniejszą liczbą artefaktów, co czyni go idealnym do generowania naturalnie wyglądających
  krajobrazów i ciał niebieskich.

  ## Główne funkcje

  - **Algorytm SurfaceNets** - generowanie gładkich mesh'y z danych voxelowych poprzez
    interpolację przecięć powierzchni
  - **Wielowątkowość (C# Job System + Burst)** – generowanie ciągiem gęstości terenu
    oraz ekstrakcji siatki.
  - **Zero-Copy Mesh Generation (`Mesh.MeshDataArray`)** - pominięcie zarządzanych tablic C# (`Vector3[]`, `int[]`)
  - **System chunków** - dynamiczne ładowanie i wyładowywanie terenu wokół gracza
  - **Simplex Noise** - proceduralne generowanie gęstości terenu z warstwami szumu FractalNoise
  - **Generator ciał niebieskich** - wsparcie dla 6 typów obiektów:
    - Meteoroid (~8m) - małe skały
    - Small Asteroid (~25m) - średnie asteroidy
    - Asteroid (~100m) - duże asteroidy
    - Planetoid (~500m) - małe planety
    - Moon (~2000m) - księżyce
    - Comet (~15m) - komety częściowo pokryte lodem
  - **Optymalizacje**:
    - Przełączany culling pustych i pełnych chunków
    - Render distance jako kula nie sześcian
    - System LOD (3 poziomy szczegółowości) (wip)
    - Dynamiczne zwalnianie odległych chunków

  ## Architektura
  
  ### Przepływ danych

  1. Player Position
  2. TerrainGenerator - sprawdza chunki w render distance
  3. GenerateChunkAsync (async Awaitable)
     - DensityJob
     - MeshJob
     - chunk.SetMesh

  ### Komponenty systemu

  #### *TerrainGenerator* - Główny kontroler całego systemu
  - Zarządza dynamicznym ładowaniem chunków wokół gracza
  - Optymalizuje pamięć poprzez usuwanie pustych/pełnych chunków
  - Zarządza kolekcjami chunkow np: załadowane, zmodyfikowane, zniszczone i w trakcie generowania, 

  #### *ChunkSN* - Pojedynczy chunk terenu (16³ voxeli)
  - Stałe rozmiary: Size=16, VoxelArraySize=17, DensityArraySize=18
  - Inicjalizuje mesh z jobów i komponenty Unity

  #### *SurfaceNetsGenerator* - Klasa pośrednicząca
  - Alokuje niezbędne bufory natywne (MeshDataArray, NativeArray<Point>, NativeList<float3>, NativeList<int>).
  - Łączy zależności (JobHandle) między DensityJob i MeshJob, zapewniając wykonanie sekwencji jedna po drugiej bez blokowania głównego wątku.
  
  #### *CelestialBodyGenerator* & *BurstSimplexNoise*
  - Warstwa matematyczna świata generująca wartości gęstości na podstawie pozycji globalnej wierzchołka, wybranego profilu ciała niebieskiego.

  ### Podstawowa konfiguracja

  1. Dodaj prefab TerrainGenerator na scene
  2. Skonfiguruj TerrainGenerator:
     - Body Data - Seed oraz typ ciała (Meteoroid, Asteroid, Moon itp.)
     - Destroy Air - czy omijać chunki które są całe powietrzem
     - Destroy Solid - czy omijać chunki pod ziemią
     - Render Distance - promień generowania świata wokół gracza liczony w chunkach.
     - Player - transform.position dla render distance
  4. Stwórz dowolny obiekt i przypisz jako Player
  5. Uruchom scenę - teren będzie generowany wokół gracza
  

  ### Technologie

  - Silnik: Unity 6.3.11f1 (Universal Render Pipeline)
  - C# Job System - wielowątkowe przetwarzanie
  - Burst Compiler - kompilacja do natywnego kodu maszynowego
  - NativeCollections - zero-copy transfer między jobami
  - Simplex Noise - gładki, proceduralny szum bez artefaktów
  - SurfaceNets Algorithm - dual contouring dla voxeli

  ### Znane ograniczenia

  - Brak systemu modyfikacji terenu w runtime - w trakcie
  - LOD z 3 stopniami szczegółowości (1, 0.5, 0.25) - w trakcie
  - liczenie normali tworzy widoczne szwy pomiędzy chunkami
  - Brak wsparcia dla tekstur
  - Mesh bez siatki UV
  - Object Pooling dla chunków - całkowita eliminacja Instantiate i Destroy na rzecz puli obiektów.
  (odchudzenie operacji na glownym wątku)

