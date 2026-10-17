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
    /// Спавн волны врагов: count юнитов в СЛУЧАЙНЫХ клетках кольца шириной
    /// edgeRingWidth клеток по периметру карты. Статы НЕ масштабируются —
    /// берутся из префаба как есть (maxHp = 5). Каждый враг занимает
    /// уникальную клетку: выбранная клетка исключается из пула кандидатов.
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

        // Пул свободных клеток края: берём по одной случайной клетке на врага
        // и удаляем её из пула — гарантируем, что никто не заспавнится друг на друге.
        var edgePool = CollectEdgeCells();

        int spawned = 0;

        for (int i = 0; i < count; i++)
        {
            Vector2Int? cell = PickRandomFromPool(edgePool);

            if (cell == null)
            {
                Debug.LogWarning($"[SquadSpawner] Край карты занят полностью — враг {i + 1}/{count} не заспавнен.");
                break; // кандидаты кончились, остальных спавнить некуда
            }

            GameObject go = Instantiate(unitPrefab);
            go.transform.position = gm.GridToWorld(cell.Value);

            Unit unit = go.GetComponent<Unit>();
            if (unit == null) unit = go.AddComponent<Unit>();

            // НИКАКИХ МНОЖИТЕЛЕЙ! Статы берутся из префаба (maxHp = 5).
            unit.team = Team.Enemy;
            unit.Facing = new Vector2Int(0, 1);
            unit.matHit = matHit;
            unit.Init(cell.Value, enemySquad);   // сначала Init — строит кэш
            unit.SetDefaultVisual(matEnemy);     // потом краска — кэш уже есть

            enemySquad.Add(unit);
            spawned++;
        }

        Debug.Log($"[SquadSpawner] Волна: заспавнено {spawned}/{count} врагов (край карты, кольцо {edgeRingWidth} кл.)");
    }

    [Header("Edge Spawn")]
    [Tooltip("Ширина кольца по периметру карты (клеток), внутри которого спавнятся враги волн.")]
    public int edgeRingWidth = 3;

    /// <summary>
    /// Собрать список всех СВОБОДНЫХ клеток в кольце шириной edgeRingWidth
    /// по периметру карты: x < ring, x >= size-ring, y < ring или y >= size-ring.
    /// Исключаются занятые (GetOccupant != null) и заблокированные клетки.
    /// </summary>
    private List<Vector2Int> CollectEdgeCells()
    {
        var result = new List<Vector2Int>();
        var gm = GridManager.Instance;
        if (gm == null) return result;

        int ring = Mathf.Max(1, edgeRingWidth);
        int size = gm.size;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool onEdge = x < ring || x >= size - ring || y < ring || y >= size - ring;
                if (!onEdge) continue;

                var cell = new Vector2Int(x, y);

                if (gm.GetOccupant(cell) != null || gm.IsBlocked(cell))
                {
                    continue;
                }

                result.Add(cell);
            }
        }

        if (result.Count == 0)
        {
            Debug.LogWarning("[SquadSpawner] Нет свободных клеток в кольце спавна по краю карты.");
        }

        return result;
    }

    /// <summary>
    /// Взять случайную клетку из пула и УДАЛИТЬ её из пула (чтобы следующий
    /// враг не занял ту же клетку). null, если пуст — край весь занят.
    /// </summary>
    private static Vector2Int? PickRandomFromPool(List<Vector2Int> pool)
    {
        if (pool == null || pool.Count == 0)
        {
            return null;
        }

        int index = Random.Range(0, pool.Count);
        Vector2Int cell = pool[index];
        pool.RemoveAt(index); // swap-remove: O(1), порядок не важен
        return cell;
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