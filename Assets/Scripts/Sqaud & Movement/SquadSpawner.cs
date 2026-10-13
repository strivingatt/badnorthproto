using UnityEngine;

/// <summary>
/// Спавнит стартовый отряд: блок 4x2 (ширина 4 по X, глубина 2 по Y сетки)
/// от клетки spawnOrigin. Позиционирует юнитов через GridManager.GridToWorld,
/// резервирует за ними клетки и собирает в Squad.
/// Плюс дебаг-спавн отряда врагов по клавише E.
/// </summary>
public class SquadSpawner : MonoBehaviour
{
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
        // Дебаг: клавиша E спавнит отряд врагов.
        if (Input.GetKeyDown(KeyCode.E))
        {
            SpawnEnemySquad();
        }
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
    /// Дебаг-спавн отряда врагов: линия из 4 юнитов на y=4 лицом к игроку (+Z).
    /// Зона атаки врага с клетки (x,4) при Facing=(0,1): (x-1,5), (x,5), (x+1,5).
    /// Контакт с игроком возможен, когда игрок стоит на y=5.
    /// </summary>
    private void SpawnEnemySquad()
    {
        var gm = GridManager.Instance;
        if (gm == null) return;

        // Squad — MonoBehaviour, поэтому создаём его через GameObject + AddComponent
        // (как у игроков в SpawnSquad): у «голого» new Squad() нет GameObject,
        // не работают Unity-жизненные циклы и его не найти через FindObjectOfType.
        var enemySquadGO = new GameObject("EnemySquad");
        var enemySquad = enemySquadGO.AddComponent<Squad>();

        int baseX = 10;
        int baseY = 4;

        for (int i = 0; i < 4; i++)
        {
            Vector2Int cell = new Vector2Int(baseX + i, baseY);
            if (!gm.IsFree(cell))
            {
                Debug.LogWarning($"[SquadSpawner] Клетка {cell} занята/заблокирована — пропуск");
                continue;
            }

            GameObject go = Instantiate(unitPrefab);
            go.transform.position = gm.GridToWorld(cell);

            Unit unit = go.GetComponent<Unit>();
            if (unit == null) unit = go.AddComponent<Unit>();

            unit.SetDefaultVisual(matEnemy);   // родной материал врага (красный)
            unit.matHit = matHit;
            unit.team = Team.Enemy;
            unit.Facing = new Vector2Int(0, 1); // смотрят на +Z (к игроку)
            unit.Init(cell, enemySquad);

            enemySquad.Add(unit);
        }

        Debug.Log($"[SquadSpawner] Заспавнен отряд врагов на y={baseY}");
    }
}