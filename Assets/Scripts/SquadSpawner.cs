using UnityEngine;

/// <summary>
/// Спавнит стартовый отряд: блок 4x2 (ширина 4 по X, глубина 2 по Y сетки)
/// от клетки spawnOrigin. Позиционирует юнитов через GridManager.GridToWorld,
/// резервирует за ними клетки и собирает в Squad.
/// </summary>
public class SquadSpawner : MonoBehaviour
{
    [Header("Spawn Settings")]
    public GameObject unitPrefab;
    public Material matUnit;
    public Vector2Int spawnOrigin = new Vector2Int(10, 10);

    [Header("Combat (Шаг 8)")]
    /// <summary>Материал юнитов вражеской команды (для дебаг-спавна по клавише E).</summary>
    public Material matEnemy;

    /// <summary>Материал «вспышки» при получении урона — назначается всем юнитам.</summary>
    public Material matHit;

    private const int BlockWidth = 4;  // ширина блока по оси X сетки
    private const int BlockHeight = 2; // глубина блока по оси Y сетки

    private void Start()
    {
        SpawnSquad();
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
                    unit.SetVisual(matUnit);
                }

                // Шаг 8: материал вспышки при уроне. team уже Player по умолчанию.
                unit.matHit = matHit;

                squad.Add(unit);
                occupiedCount++;
            }
        }

        Debug.Log($"[SquadSpawner] Отряд заспавнен: клеток занято успешно — {occupiedCount}, " +
                  $"попыток отклонено — {rejectedCount}.");
    }

    // ------------------------------------------------------------------
    // Шаг 8: дебаг-спавн вражеского отряда по клавише E.
    // ------------------------------------------------------------------

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.E))
        {
            SpawnEnemySquad();
        }
    }

    /// <summary>
    /// Дебаг: спавнит отряд из 4 врагов линией на y=4 (baseX..baseX+3),
    /// лицом НА ИГРОКА. Клетки, занятые/заблокированные игроком или камнями,
    /// пропускаются с warning. Повторное нажатие E спавнит ещё один отряд
    /// (или его остатки, если клетки свободны).
    ///
    /// САМОПРОВЕРКА ЗОН АТАКИ (пересчитана вручную и проверена симуляцией;
    /// формула Unit.GetAttackCells: cell = GridPos + Facing*1 + right*r,
    /// r=-1..1, right = (Facing.y, -Facing.x)). Ось Y сетки = мир +Z; игрок
    /// спавнится на больших y, поэтому «смотреть на игрока» = Facing=(0,+1).
    ///
    /// Сценарий ТЗ «игрок на y=6 лицом (0,-1), враги на y=5 лицом (0,1)»:
    /// - Игрок в (x,6): right=(-1,0), зона = {(x-1,5),(x,5),(x+1,5)} — ряд y=5.
    /// - Враг  в (x,5): right=(1,0),  зона = {(x-1,6),(x,6),(x+1,6)} — ряд y=6.
    /// Зоны ПЕРЕКРЫВАЮТСЯ с позициями противника: фронт каждого юнита смещён
    /// на ±1 клетку по X (крайние клетки зоны — диагонали), поэтому при
    /// шеренгах x=10..13 боевые пары возникают со сдвигом на 1: например,
    /// игрок (10,6) бьёт врага (11,5), а враг (10,5) бьёт игрока (11,6).
    /// Внутренние юниты шеренг получают до 2 целей одновременно — бой идёт
    /// в обе стороны (в симуляции: 8 пар «игрок->враг» и 8 пар «враг->игрок»).
    /// Для сравнения: «враги на y=4» (дистанция 2) — зоны бьют в пустой ряд
    /// y=5, контакта НЕТ. Поэтому после E подведите отряд drag'ом вплотную
    /// к врагам (на y=5).
    /// </summary>
    private void SpawnEnemySquad()
    {
        var gm = GridManager.Instance;

        if (gm == null)
        {
            return;
        }

        // Squad — MonoBehaviour, поэтому ему нужен GameObject (тот же приём,
        // что в основном SpawnSquad).
        var enemySquadGO = new GameObject("Squad_Enemy");
        Squad enemySquad = enemySquadGO.AddComponent<Squad>();

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
            go.name = $"Enemy_{cell.x}_{cell.y}";
            go.transform.position = gm.GridToWorld(cell);

            Unit unit = go.GetComponent<Unit>();
            if (unit == null) unit = go.AddComponent<Unit>();

            unit.SetVisual(matEnemy);
            unit.matHit = matHit;
            unit.team = Team.Enemy;
            // Враги на y=4, игрок спавнится на y>=10 (большие y) — значит «на
            // игрока» = в сторону РАСТУЩИХ y: Facing=(0,+1) (мир +Z). С этим
            // facing зона атаки врага (x,4) = ряд y=5 — прямо перед ним.
            unit.Facing = new Vector2Int(0, 1);
            unit.Init(cell, enemySquad);

            enemySquad.Add(unit);
        }

        Debug.Log($"[SquadSpawner] Заспавнен отряд врагов на y={baseY}");
    }
}
