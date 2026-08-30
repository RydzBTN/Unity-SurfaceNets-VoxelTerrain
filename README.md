# SurfaceNets Terrain Generator 
## WIP

<img width="1261" height="972" alt="image" src="https://github.com/user-attachments/assets/ab40477f-b23f-4721-b453-9447848562ca" />

  Generator terenu 3D dla Unity wykorzystujący algorytm SurfaceNets do proceduralnego generowania
  gładkich mesh'y z danych voxelowych.

---
  ## Opis

  Projekt implementuje algorytm SurfaceNets do generowania terenu w czasie rzeczywistym. 
  W przeciwieństwie do klasycznego Marching Cubes, SurfaceNets tworzy gładsze siatki z
  mniejszą liczbą artefaktów, co czyni go idealnym do generowania naturalnie wyglądających 
  ciał niebieskich.

  ## Główne funkcje

  - **Algorytm SurfaceNets** - generowanie gładkich mesh'y z danych voxelowych poprzez interpolację przecięć powierzchni
  - **System chunków** - dynamiczne ładowanie i wyładowywanie terenu wokół gracza
  - **Wielowątkowość (C# Job System + Burst)** – generowanie ciągiem gęstości terenu oraz ekstrakcji siatki.
  - **Zero-Copy Mesh Generation (`Mesh.MeshDataArray`)** - pominięcie zarządzanych tablic C# (`Vector3[]`, `int[]`)
  - **System powłok** – wydajna detekcja zmian na granicy dystansu widzenia, przy poruszaniu się sprawdzane są
  tylko krawędzie sfery zamiast iterowania przez całą kule w każdej klatce.
  - **System LOD** – dynamiczny dobór kroku próbkowania (`lodStep`) w zależności od odległości od gracza,
  aktualizowany na bieżąco przez dedykowane powłoki LOD.
  - **Pula obiektów (`UnityEngine.Pool.IObjectPool`)** – ponowne wykorzystywanie instancji chunków (`ChunkSN`)
  bez narzutu na GC i częstego wywoływania `Instantiate`/`Destroy`.
  - **Simplex Noise** - proceduralne generowanie gęstości terenu z warstwami szumu FractalNoise
  - **Generator ciał niebieskich** - wsparcie dla 6 typów obiektów:
    - Meteoroid (~8m) - małe skały
    - Small Asteroid (~25m) - średnie asteroidy
    - Asteroid (~100m) - duże asteroidy
    - Planetoid (~500m) - małe planety
    - Moon (~2000m) - księżyce
    - Comet (~15m) - komety częściowo pokryte lodem
  - **Optymalizacje**:
    - Culling pustych i pełnych chunków
    - Render distance jako kula nie sześcian
    - System LOD (2 poziomy szczegółowości)
    - Dynamiczne zwalnianie odległych chunków
    - Object Pooling dla chunków
    - limit jednoczesnych generacji


  ## Architektura
  
  ### Przepływ danych
  1. **Pozycja gracza (`Transform player`)** – przeliczana na lokalny indeks chunka (`WorldPosToChunkIndex`).
  2. **TerrainGenerator (`UpdateChunksAroundPlayer`)**:
      - Przy małym przesunięciu: aktualizacja na podstawie powłok (`StepAxis` / `Step` na `_renderShell`, `_unloadShell`, `_lod0Shell`).
      - Przy dużym przesunięciu (np. teleportacja): pełna przebudowa (`FullRebuildChunks`).
  3. **Kolejka renderowania (`_pendingQueue` / `_pendingSet`)** – brak duplikatów i ograniczenie do limitu `maxConcurrentGen`.
  4. **`GenerateChunkAsync` (asynchroniczny task)**:
      - `ChunkMeshBuilder.BuildAsync` (DensityJob + MeshJob).
      - Weryfikacja spójności stanu i `GenId`.
      - Przypisanie siatki (`chunk.SetMesh`) lub oznaczenie jako `ChunkState.Air` / `ChunkState.Solid` i zwolnienie do puli.

  ### Komponenty systemu

  #### *TerrainGenerator*
  - Główny zarządca świata, cyklu życia chunków i pamięci.
  - Odpowiada za konstrukcję powłok (`BuildShell`), sterowanie poziomami LOD,
  kolejkowanie generacji oraz obsługę puli obiektów (`_chunkPool`).

  #### *ChunkSN*
  - Reprezentacja pojedynczego chunka w scenie (domyślny rozmiar 16³ voxeli).
  - Zarządza komponentami `MeshFilter`, `MeshRenderer` oraz aplikowaniem wygenerowanego `Mesh.MeshDataArray`.

  #### *ChunkMeshBuilder*
  - Odpowiada za asynchroniczne przygotowanie danych siatki, alokację buforów natywnych, uruchomienie
  łańcucha jobów (próbkowanie gęstości + SurfaceNets) oraz zwrócenie wyniku w postaci `MeshBuildResult`.
  
  #### *CelestialBodyGenerator* & *BurstSimplexNoise*
  - Warstwa matematyczna świata generująca wartości gęstości na podstawie pozycji globalnej wierzchołka,
  wybranego profilu ciała niebieskiego.

  ### Podstawowa konfiguracja
  1. Dodaj prefab TerrainGenerator na scene
  2. Skonfiguruj TerrainGenerator:
     - Body Data - Seed oraz typ ciała (Meteoroid, Asteroid, Moon itp.)
     - Destroy Air - czy omijać chunki które są całe powietrzem
     - Destroy Solid - czy omijać chunki pod ziemią
     - Render Distance - promień generowania świata wokół gracza liczony w chunkach.
     - Player - transform.position dla render distance
     - **LOD (Distance & Step)**:
       - `lod0Distance` / `lod0Step` (np. dystans 4, krok 1 – pełna rozdzielczość).
       - `lod1Step` (np. krok 4 - 1/4 rozdzielczości, dla pozostałych chunków).
  3. Uruchom scenę - teren będzie generowany wokół gracza
  

  ### Technologie
  - Silnik: Unity 6.3.11f1 (Universal Render Pipeline)
  - C# Job System - wielowątkowe przetwarzanie
  - Burst Compiler - kompilacja do natywnego kodu maszynowego
  - Simplex Noise - gładki, proceduralny szum bez artefaktów
  - Algorytm SurfaceNets

  ### WIP
  - prawidłowe oznaczanie pustych chunków
  - łączenia między warstwami LOD
  - szybkie sprawdzania czy pusty
  - sprawdzanie tylko zewnętrznej warstwy render distance

 ### Znane problemy
  - **Złe oznaczanie pustych chunków** - generator działający z mniejszą dokładnością (lod2,3)
  oznacza chunka jako pusty gdzie z docelową dokładnością (lod0) tak na prawde ma on siate.
  - **Precyzja oznaczania pustych chunków przy niskim LOD** – próbkowanie z większym krokiem (`lod1Step`)
  może pominąć cienkie warstwy terenu i przedwcześnie sklasyfikować chunk jako całkowicie pusty (`Air`) lub pełny (`Solid`).




