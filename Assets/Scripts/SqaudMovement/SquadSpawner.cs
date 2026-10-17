using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Спавнит стартовый отряд: блок 4x2 (ширина 4 по X, глубина 2 по Y сетки)
/// от клетки spawnOrigin. Позиционирует юнитов через GridManager.GridToWorld,
/// резервирует за ними клетки и собирает в Squad.
/// Плюс дебаг-спавн отряда врагов по клавише E.
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
    public GameObject unitPrefab;
    public Material matUnit;
    public Vector2Int spawnOrigin = new Vector2Int(10, 10);

    [Header("Combat Visuals")]
    [Tooltip("Материал вражеских юнитов (дебаг-спавн по E).")]
    public Material matEnemy;

    [Tooltip("Материал вспышки при получении урона (назначается всем юнитам).")]
    public Material matHit;

    private const int BlockWidth = 4;  // ширина блока по оси X сетки
    private const int BlockHeight = 2; // глубина блока по оси Y сетки

    private void Start()
    {
        SpawnSquad();
    }

    private void Update()
    {
        // Дебаг-клавиша E переехала в InputController: теперь она запускает
        // первую волну через WaveManager.StartNextWave(). SpawnEnemySquad()
        // оставлен приватной обёрткой для обратной совместимости.
    }

    private void SpawnSquad()
    {
        if (unitPrefab == null)
        {
            Debug.LogError("[SquadSpawner] unitPrefab не назначен — спавн отменён.");
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

                var go = Instantiate(unitPrefab);
                go.name = $"Unit_{cell.x}_{cell.y}";

                // Позиционируем в центр клетки ещё до Init, чтобы юнит был «на месте».
                go.transform.position = GridManager.Instance.GridToWorld(cell);

                var unit = go.GetComponent<Unit>();
                if (unit == null)
                {
                    unit = go.AddComponent<Unit>();
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
    /// Спавн волны врагов: count юнитов КОМПАКТНОЙ ГРУППОЙ в одном случайном
    /// месте на краю карты (кольцо шириной edgeRingWidth клеток). Берётся
    /// случайная anchor-клетка края, вокруг неё строится квадрат
    /// spawnBlockSize x spawnBlockSize, сдвинутый ВНУТРЬ карты (в углу спавн
    /// не выходит за границы). Внутри квадрата спавнятся только свободные
    /// клетки (IsFree) — каждая уникальна. Если свободных меньше count —
    /// выбирается новая anchor (максимум spawnAnchorAttempts попыток); после
    /// исчерпания попыток спавнится сколько влезло + warning.
    /// Статы НЕ масштабируются — берутся из префаба как есть (maxHp = 5).
    /// </summary>
    public void SpawnWave(int count)
    {
        var gm = GridManager.Instance;
        if (gm == null) return;

        if (unitPrefab == null)
        {
            Debug.LogError("[SquadSpawner] unitPrefab не назначен — волна отменена.");
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
                    if (gm.IsFree(cell)) cells.Add(cell);
                }
            }

            while (cells.Count > 0 && spawned < count)
            {
                int index = Random.Range(0, cells.Count);
                Vector2Int cell = cells[index];
                cells.RemoveAt(index); // этот враг занял клетку — остальные не могут

                GameObject go = Instantiate(unitPrefab);
                go.transform.position = gm.GridToWorld(cell);

                Unit unit = go.GetComponent<Unit>();
                if (unit == null) unit = go.AddComponent<Unit>();

                // НИКАКИХ МНОЖИТЕЛЕЙ! Статы берутся из префаба (maxHp = 5).
                unit.team = Team.Enemy;
                unit.Facing = new Vector2Int(0, 1);
                unit.matHit = matHit;
                unit.Init(cell, enemySquad);   // сначала Init — строит кэш
                unit.SetDefaultVisual(matEnemy); // потом краска — кэш уже есть

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
                if (gm.IsFree(cell)) candidates.Add(cell);
            }
        }

        if (candidates.Count == 0)
        {
            return null;
        }

        return candidates[Random.Range(0, candidates.Count)];
    }

    /// <summary>
    /// Обратная совместимость: дебаг-спавн отряда врагов (клавиша E) —
    /// теперь обёртка над SpawnWave.
    /// </summary>
    private void SpawnEnemySquad()
    {
        SpawnWave(4);
    }
}