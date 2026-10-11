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

                squad.Add(unit);
                occupiedCount++;
            }
        }

        Debug.Log($"[SquadSpawner] Отряд заспавнен: клеток занято успешно — {occupiedCount}, " +
                  $"попыток отклонено — {rejectedCount}.");
    }
}
