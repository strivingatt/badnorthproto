using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Спавнит стартовый отряд: блок 4x2 (ширина 4 по X, глубина 2 по Y сетки)
/// от клетки spawnOrigin. Позиционирует юнитов через GridManager.GridToWorld,
/// резервирует за ними клетки и собирает в Squad.
/// Плюс спавн волн врагов: SpawnWave(count) вызывается из WaveManager.
///
/// ТИП ЮНИТА ОПРЕДЕЛЯЕТСЯ ПРЕФАБОМ: playerUnitPrefab и enemyUnitPrefab независимы,
/// боевой класс (Militia/Spearman/Warrior/Slinger) и статы берутся из компонента Unit
/// на соответствующем префабе. Код НЕ добавляет боевые классы на лету — иначе на одном
/// GameObject оказалось бы два Unit-компонента (двойной урон/кулдаун/EnemyAI).
/// </summary>
public class SquadSpawner : MonoBehaviour
{
    /// <summary>Синглтон — доступ через SquadSpawner.Instance (нужен WaveManager).</summary>
    public static SquadSpawner Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    [Header("Spawn Settings")]
    [Tooltip("Префаб юнитов ИГРОКА. Боевой класс (Militia/Spearman/Warrior/Slinger) " +
             "и его статы берутся из компонента Unit НА ЭТОМ префабе.")]
    [SerializeField] private GameObject playerUnitPrefab;

    [Tooltip("Префаб ВРАЖЕСКИХ юнитов волн. Класс и статы — из компонента Unit на этом " +
             "префабе, поэтому тип врага НЕ зависит от типа юнитов игрока. " +
             "Пусто — волна не спавнится (ошибка в консоли).")]
    [SerializeField] private GameObject enemyUnitPrefab;

    public Material matUnit;
    public Vector2Int spawnOrigin = new Vector2Int(10, 10);

    [Header("Combat Visuals")]
    [Tooltip("Материал вражеских юнитов волн. Если не назначен — остаётся родной материал префаба врага.")]
    public Material matEnemy;

    [Tooltip("Материал вспышки при получении урона (назначается всем юнитам).")]
    public Material matHit;

    private const int BlockWidth = 4;  // ширина блока по оси X сетки
    private const int BlockHeight = 3; // глубина блока по оси Y сетки (4×3 = 12 бойцов, как в GDD.md)

    // Корутина ожидания генерации рельефа: TerrainGenerator строит карту и
    // блокирует воду в GridManager; отряд и волны должны спавниться уже по
    // готовой сетке blocked-клеток, иначе юниты могут встать на воду.
    private const int TerrainWaitMaxFrames = 10;

    private void Start()
    {
        StartCoroutine(SpawnAfterTerrain());
    }

    /// <summary>
    /// Ждём появления TerrainGenerator (максимум несколько кадров), затем
    /// спавним стартовый отряд. Если генератора на сцене нет — спавним сразу
    /// (обратная совместимость со старой сценой без тайлов).
    /// </summary>
    private System.Collections.IEnumerator SpawnAfterTerrain()
    {
        var generator = TerrainGenerator.Instance;
        int waited = 0;

        while (generator == null && waited < TerrainWaitMaxFrames)
        {
            yield return null;
            waited++;
            generator = TerrainGenerator.Instance;
        }

        SpawnSquad();
    }

    private void SpawnSquad()
    {
        if (playerUnitPrefab == null)
        {
            Debug.LogError($"[{nameof(SquadSpawner)}] playerUnitPrefab не назначен — спавн отменён.");
            return;
        }

        // Класс юнита задаётся префабом: проверяем его ДО цикла, иначе каждый инстанс
        // пришлось бы уничтожать с ошибкой в консоли.
        if (playerUnitPrefab.GetComponent<Unit>() == null)
        {
            Debug.LogError($"[{nameof(SquadSpawner)}] На префабе игрока '{playerUnitPrefab.name}' " +
                           "нет компонента Unit — спавн отменён.");
            return;
        }

        if (GridManager.Instance == null)
        {
            Debug.LogError("[SquadSpawner] GridManager.Instance == null — спавн отменён.");
            return;
        }

        var squadGO = new GameObject("Squad");
        var squad = squadGO.AddComponent<Squad>();

        int occupiedCount = 0;  // сколько клеток занято успешно
        int rejectedCount = 0;  // сколько попыток размещения отклонено

        for (int y = 0; y < BlockHeight; y++)
        {
            for (int x = 0; x < BlockWidth; x++)
            {
                Vector2Int cell = new Vector2Int(spawnOrigin.x + x, spawnOrigin.y + y);

                // Пред-проверка доступности клетки до спавна.
                if (!GridManager.Instance.IsFree(cell))
                {
                    Debug.LogError($"[SquadSpawner] Клетка {cell} недоступна (занята/заблокирована/вне границ) — попытка отклонена.");
                    rejectedCount++;
                    continue;
                }

                var go = Instantiate(playerUnitPrefab);
                go.name = $"Unit_{cell.x}_{cell.y}";

                // Позиционируем в центр клетки ещё до Init, чтобы юнит был «на месте».
                go.transform.position = GridManager.Instance.GridToWorld(cell);

                // Класс берётся из префаба (Militia/Spearman/Warrior/Slinger),
                // никаких AddComponent на лету.
                var unit = GetUnitOrNull(go);

                if (unit == null)
                {
                    Debug.LogError($"[{nameof(SquadSpawner)}] Инстанс префаба игрока без компонента " +
                                   $"Unit — клетка {cell} пропущена.");
                    rejectedCount++;
                    continue;
                }

                unit.Init(cell, squad);

                if (matUnit != null)
                {
                    // Родной материал игрока (белый): запоминается для восстановления
                    // после снятия выделения.
                    unit.SetDefaultVisual(matUnit);
                }

                if (matHit != null)
                {
                    unit.matHit = matHit;
                }

                squad.Add(unit);
                occupiedCount++;
            }
        }

        Debug.Log($"[SquadSpawner] Отряд заспавнен: клеток занято успешно — {occupiedCount}, " +
                  $"попыток отклонено — {rejectedCount}.");
    }

    /// <summary>
    /// Проходимость клетки для спавна: клетка не должна быть заблокирована
    /// (вода/камень) и должна быть свободна от юнитов. IsFree покрывает
    /// блокировку terrain-генератором и ObstaclePlacer'ом; отдельная проверка
    /// IsBlocked добавлена явно по требованию «враги только на проходимых
    /// клетках» — если клетка заблокирована водой, она не попадёт в кандидаты,
    /// и спавн выберет другую.
    /// </summary>
    private static bool IsSpawnableCell(GridManager gm, Vector2Int cell)
    {
        if (gm.IsBlocked(cell)) return false; // вода/камень — не спавнимся
        return gm.IsFree(cell);               // + границы и занятость юнитами
    }

    /// <summary>
    /// Спавн волны врагов: count юнитов КОМПАКТНОЙ ГРУППОЙ в одном случайном
    /// месте на краю карты (кольцо шириной edgeRingWidth клеток). Берётся
    /// случайная anchor-клетка края, вокруг неё строится квадрат
    /// spawnBlockSize x spawnBlockSize, сдвинутый ВНУТРЬ карты (в углу спавн
    /// не выходит за границы). Внутри квадрата спавнятся только свободные
    /// клетки (IsFree) — каждая уникальна. Если свободных меньше count —
    /// выбирается новая anchor (максимум spawnAnchorAttempts попыток); после
    /// исчерпания попыток спавнится сколько влезло + warning.
    /// Статы НЕ масштабируются — берутся из префаба врага (enemyUnitPrefab) как есть.
    /// </summary>
    public void SpawnWave(int count)
    {
        var gm = GridManager.Instance;
        if (gm == null) return;

        if (enemyUnitPrefab == null)
        {
            Debug.LogError($"[{nameof(SquadSpawner)}] enemyUnitPrefab не назначен — волна отменена.");
            return;
        }

        // Класс врага задаётся префабом врага: проверяем ДО цикла инстансов.
        if (enemyUnitPrefab.GetComponent<Unit>() == null)
        {
            Debug.LogError($"[{nameof(SquadSpawner)}] На префабе врага '{enemyUnitPrefab.name}' " +
                           "нет компонента Unit — волна отменена.");
            return;
        }

        // Squad — MonoBehaviour, поэтому создаём его через GameObject + AddComponent
        // (как у игроков в SpawnSquad): у «голого» new Squad() нет GameObject,
        // не работают Unity-жизненные циклы и его не найти через FindObjectOfType.
        var enemySquadGO = new GameObject("EnemySquad_Wave");
        var enemySquad = enemySquadGO.AddComponent<Squad>();

        int spawned = 0;

        for (int attempt = 0; attempt < spawnAnchorAttempts && spawned < count; attempt++)
        {
            Vector2Int? anchor = PickRandomAnchorCell();

            if (anchor == null)
            {
                Debug.LogWarning("[SquadSpawner] Нет свободных клеток спавна по краю карты — волна отменена.");
                break;
            }

            // Компактная зона спавна: квадрат spawnBlockSize x spawnBlockSize,
            // прижатый (clamped) к границам карты — в углу сдвигается внутрь.
            int half = spawnBlockSize / 2;
            int minX = Mathf.Clamp(anchor.Value.x - half, 0, gm.size - spawnBlockSize);
            int minY = Mathf.Clamp(anchor.Value.y - half, 0, gm.size - spawnBlockSize);

            // Собираем свободные клетки квадрата; IsFree учитывает занятость
            // и блокировку, а удаление из списка гарантирует уникальность.
            var cells = new List<Vector2Int>();
            for (int y = minY; y < minY + spawnBlockSize; y++)
            {
                for (int x = minX; x < minX + spawnBlockSize; x++)
                {
                    var cell = new Vector2Int(x, y);
                    // Только проходимые клетки: заблокированная вода/камень
                    // отбраковывается, спавн берёт другую клетку блока.
                    if (IsSpawnableCell(gm, cell)) cells.Add(cell);
                }
            }

            while (cells.Count > 0 && spawned < count)
            {
                int index = Random.Range(0, cells.Count);
                Vector2Int cell = cells[index];
                cells.RemoveAt(index); // этот враг занял клетку — остальные не могут

                GameObject go = Instantiate(enemyUnitPrefab);
                go.transform.position = gm.GridToWorld(cell);

                // Класс врага приходит ИЗ ПРЕФАБА ВРАГА и не зависит от префаба игрока.
                Unit unit = GetUnitOrNull(go);

                if (unit == null)
                {
                    Debug.LogError($"[{nameof(SquadSpawner)}] Инстанс префаба врага без компонента " +
                                   $"Unit — клетка {cell} пропущена.");
                    continue;
                }

                // НИКАКИХ МНОЖИТЕЛЕЙ! Статы берутся из класса-префаба.
                unit.team = Team.Enemy;
                unit.Facing = new Vector2Int(0, 1);
                unit.matHit = matHit;
                unit.Init(cell, enemySquad);   // сначала Init — строит кэш

                // Красим врага ТОЛЬКО если материал задан: иначе остаётся родной
                // материал префаба (кэш для восстановления уже построен в Init).
                if (matEnemy != null)
                {
                    unit.SetDefaultVisual(matEnemy);
                }

                enemySquad.Add(unit);
                spawned++;
            }
            // Если cells опустел раньше count — внешний цикл выберет новую anchor.
        }

        if (spawned < count)
        {
            Debug.LogWarning($"[SquadSpawner] Не хватило места в зонах спавна: " +
                             $"заспавнено {spawned}/{count} врагов за {spawnAnchorAttempts} попыток.");
        }

        Debug.Log($"[SquadSpawner] Волна: заспавнено {spawned}/{count} врагов (кучкой, край карты, блок {spawnBlockSize}x{spawnBlockSize})");
    }

    [Header("Edge Spawn")]
    [Tooltip("Ширина кольца по периметру карты (клеток), внутри которого выбирается точка спавна волн.")]
    public int edgeRingWidth = 3;

    [Tooltip("Размер компактной зоны спавна (квадрат N x N клеток вокруг anchor-клетки).")]
    public int spawnBlockSize = 4;

    [Tooltip("Максимум попыток выбрать anchor-клетку с достаточным числом свободных клеток.")]
    public int spawnAnchorAttempts = 10;

    /// <summary>
    /// Случайная anchor-клетка в кольце шириной edgeRingWidth по периметру
    /// карты (x < ring, x >= size-ring, y < ring или y >= size-ring), сама
    /// свободная (IsFree). null, если таких клеток нет.
    /// </summary>
    private Vector2Int? PickRandomAnchorCell()
    {
        var gm = GridManager.Instance;
        if (gm == null) return null;

        int ring = Mathf.Max(1, edgeRingWidth);
        int size = gm.size;

        var candidates = new List<Vector2Int>();

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool onEdge = x < ring || x >= size - ring || y < ring || y >= size - ring;
                if (!onEdge) continue;

                var cell = new Vector2Int(x, y);
                // Anchor тоже только на проходимой клетке (не в воде).
                if (IsSpawnableCell(gm, cell)) candidates.Add(cell);
            }
        }

        if (candidates.Count == 0)
        {
            return null;
        }

        return candidates[Random.Range(0, candidates.Count)];
    }

    /// <summary>
    /// Достать боевой класс с инстанса префаба.
    /// Класс определяется ИСКЛЮЧИТЕЛЬНО префабом (наследник Unit на корне объекта:
    /// Militia/Spearman/Warrior/Slinger). GetComponent&lt;Unit&gt; находит и наследников,
    /// поэтому AddComponent не нужен и дублей Unit на одном GameObject не возникает.
    /// Если компонента нет — инстанс уничтожается (чтобы не оставлять на сцене объект
    /// без логики) и возвращается null.
    /// </summary>
    private static Unit GetUnitOrNull(GameObject go)
    {
        Unit unit = go.GetComponent<Unit>();

        if (unit == null)
        {
            Debug.LogError($"[{nameof(SquadSpawner)}] На инстансе '{go.name}' нет компонента Unit — инстанс уничтожен.");
            Destroy(go);
            return null;
        }

        return unit;
    }
}