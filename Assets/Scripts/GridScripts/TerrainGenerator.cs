using UnityEngine;

/// <summary>
/// Процедурная генерация тайлов местности (трава/вода) через шум Перлина.
/// Тайлы — отдельные GameObject'ы из префабов grassPrefab/waterPrefab,
/// спавнятся при старте сцены (Awake). Каждый тайл получает TerrainTile.Init,
/// который сам блокирует водяные клетки в GridManager (SetBlocked).
///
/// ВАЖНО ПО ПОРЯДКУ ИНИЦИАЛИЗАЦИИ: Generate() вызывается из Awake, а
/// GridManager.Instance тоже создаётся в Awake. Если TerrainGenerator окажется
/// в сцене раньше GridManager, Instance ещё будет null — поэтому перед
/// генерацией есть ожидание в корутине-фолбэке (см. Start), но основной путь —
/// повесить TerrainGenerator на объект, идущий в иерархии после GridManager,
/// либо включить скрипт позже (execution order). Без GridManager генерация
/// откладывается до его появления.
///
/// Оптимизация (минимальная): для GPU Instancing в материале тайла включи
/// Enable GPU Instancing (галка в инспекторе материала, не в коде) — тогда
/// все тайлы одного типа уходят одним draw call.
/// LOD, чанки, pooling — потом; сейчас MVP.
/// </summary>
public class TerrainGenerator : MonoBehaviour
{
    /// <summary>Синглтон — доступ через TerrainGenerator.Instance.</summary>
    public static TerrainGenerator Instance { get; private set; }

    [Header("Map")]
    [Tooltip("Размер стороны карты в клетках. ДОЛЖЕН совпадать с GridManager.size.")]
    [SerializeField] private int mapSize = 36;

    [Header("Noise")]
    [Tooltip("Частота шума Перлина: меньше = крупнее пятна, больше = мельче детали.")]
    [SerializeField] private float noiseScale = 0.08f;

    [Tooltip("Порог воды: значение шума ниже порога = Water. ~0.35 даёт примерно треть воды.")]
    [Range(0f, 1f)]
    [SerializeField] private float waterThreshold = 0.35f;

    [Tooltip("Seed генерации. 0 = случайный при старте (фактический seed логируется — карту можно воспроизвести).")]
    [SerializeField] private int seed = 0;

    [Header("Random Rotation")]
    [Tooltip("Случайный поворот тайлов вокруг Y для разнообразия (кратен 90°, чтобы не ломать стыки сетки).")]
    [SerializeField] private bool randomRotation = false;

    [Header("Prefabs")]
    [Tooltip("Префаб проходимого тайла (tile_grass.fbx, высота 0.1). На корне должен быть TerrainTile или хотя бы MeshRenderer.")]
    [SerializeField] private GameObject grassPrefab;

    [Tooltip("Префаб непроходимого тайла (tile_water.fbx, высота 0.04). Клетка блокируется автоматически.")]
    [SerializeField] private GameObject waterPrefab;

    [Tooltip("Родитель всех тайлов на сцене (для порядка в Hierarchy).")]
    [SerializeField] private Transform tilesParent;

    /// <summary>Двумерный массив тайлов [x, z], индексы = клетки сетки.</summary>
    private TerrainTile[,] tiles;

    /// <summary>Фактически использованный seed (после подстановки случайного, если seed == 0).</summary>
    private int usedSeed;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // Если GridManager уже инициализирован (идёт раньше в порядке Awake) —
        // генерируем сразу; иначе дождёмся его в Start (см. WaitAndGenerate).
        if (tiles == null && GridManager.Instance != null)
        {
            Generate();
        }
    }

    private void Start()
    {
        // Фолбэк: GridManager мог проснуться позже нас — генерируем, как только
        // он появится. Повторный Generate() не запускается, если tiles уже построен.
        if (tiles == null)
        {
            StartCoroutine(WaitAndGenerate());
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private System.Collections.IEnumerator WaitAndGenerate()
    {
        while (GridManager.Instance == null)
        {
            yield return null;
        }

        if (tiles == null)
        {
            Generate();
        }
    }

    /// <summary>
    /// Генерация карты: шум Перлина -> тип местности -> Instantiate префаба ->
    /// TerrainTile.Init (тот блокирует воду в GridManager). Идемпотентен:
    /// повторный вызов перестраивает карту с нуля.
    /// </summary>
    public void Generate()
    {
        if (grassPrefab == null || waterPrefab == null)
        {
            Debug.LogError("[TerrainGenerator] grassPrefab/waterPrefab не назначены — " +
                           "карта не сгенерирована.");
            return;
        }

        if (GridManager.Instance == null)
        {
            Debug.LogError("[TerrainGenerator] GridManager.Instance == null — карта не сгенерирована.");
            return;
        }

        // mapSize обязан совпадать с сеткой GridManager, иначе часть тайлов
        // ляжет вне границ или карта не покроет сетку.
        if (mapSize != GridManager.Instance.size)
        {
            Debug.LogWarning($"[TerrainGenerator] mapSize={mapSize} не совпадает с " +
                             $"GridManager.size={GridManager.Instance.size} — использую size сетки.");
            mapSize = GridManager.Instance.size;
        }

        // seed == 0 -> случайный; логируем его для воспроизводимости карты.
        if (seed == 0)
        {
            usedSeed = Random.Range(1, int.MaxValue);
            Debug.Log($"[TerrainGenerator] Случайный seed: {usedSeed} " +
                      "(запишите в поле seed, чтобы воспроизвести карту).");
        }
        else
        {
            usedSeed = seed;
        }

        var rng = new System.Random(usedSeed);

        // Смещение координат шума по seed: одна и та же noiseScale, но разные
        // «вырезы» из бесконечного поля Перлина для разных seed.
        float seedOffsetX = (float)(rng.NextDouble() * 1000.0);
        float seedOffsetZ = (float)(rng.NextDouble() * 1000.0);

        // Перестройка: удалить старые тайлы, если они были.
        if (tiles != null)
        {
            for (int x = 0; x < tiles.GetLength(0); x++)
            {
                for (int z = 0; z < tiles.GetLength(1); z++)
                {
                    if (tiles[x, z] != null) Destroy(tiles[x, z].gameObject);
                }
            }
        }

        tiles = new TerrainTile[mapSize, mapSize];

        var parent = tilesParent != null ? tilesParent : transform;
        int waterCount = 0;

        for (int x = 0; x < mapSize; x++)
        {
            for (int z = 0; z < mapSize; z++)
            {
                // Шум Перлина по клетке; y-координата сетки — это мировая Z.
                float noise = Mathf.PerlinNoise(
                    x * noiseScale + seedOffsetX,
                    z * noiseScale + seedOffsetZ);

                TerrainType type = noise < waterThreshold ? TerrainType.Water : TerrainType.Grass;
                if (type == TerrainType.Water) waterCount++;

                GameObject prefab = type == TerrainType.Water ? waterPrefab : grassPrefab;
                Vector2Int cell = new Vector2Int(x, z);

                // Позиция передаётся в Init, но ставим её сразу при Instantiate,
                // чтобы лишний раз не двигать трансформ после создания.
                Quaternion rot = randomRotation
                    ? Quaternion.Euler(0f, 90f * rng.Next(0, 4), 0f) // кратно 90° — стыки не ломаются
                    : Quaternion.identity;

                var go = Instantiate(prefab, Vector3.zero, rot, parent);
                go.name = $"Tile_{x}_{z}_{type}";

                var tile = go.GetComponent<TerrainTile>();
                if (tile == null)
                {
                    tile = go.AddComponent<TerrainTile>();
                }

                // Init позиционирует тайл (GridToWorld) и блокирует воду в GridManager.
                tile.Init(cell, type);

                tiles[x, z] = tile;
            }
        }

        Debug.Log($"[TerrainGenerator] Карта {mapSize}x{mapSize} сгенерирована, " +
                  $"seed={usedSeed}, вода={waterCount} клеток " +
                  $"({100f * waterCount / (mapSize * mapSize):F1}%).");
    }

    /// <summary>Тайл в клетке или null (вне границ / не сгенерировано).</summary>
    public TerrainTile GetTile(Vector2Int cell)
    {
        if (tiles == null) return null;
        if (cell.x < 0 || cell.x >= mapSize || cell.y < 0 || cell.y >= mapSize) return null;
        return tiles[cell.x, cell.y];
    }

    /// <summary>
    /// Тип местности в клетке. Вне границ считаем Water (непроходимо),
    /// до генерации карты — Grass (сетка ещё пустая, пусть всё проходимо).
    /// В будущем здесь же появится GetMovementCost (вода = непроходима).
    /// </summary>
    public TerrainType GetTerrainType(Vector2Int cell)
    {
        var tile = GetTile(cell);
        if (tile != null) return tile.type;
        return tiles == null ? TerrainType.Grass : TerrainType.Water;
    }
}
