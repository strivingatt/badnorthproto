using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Инструмент размещения препятствий («камней»): пока зажата клавиша B,
/// клик ЛКМ по клетке ставит/убирает камень (блокирует/разблокирует клетки
/// в GridManager). Размер камня выбирается клавишами 1-5.
/// Конфликт с обычным вводом решён просто: в InputController.Update первой
/// строкой стоит if (Input.GetKey(KeyCode.B)) return; — пока активен режим
/// камней, выделение отряда и drag формации не обрабатываются.
/// </summary>
public class ObstaclePlacer : MonoBehaviour
{
    [Header("Obstacles")]
    [Tooltip("Префаб камня-препятствия (обычный 3D-объект с Renderer).")]
    public GameObject rockPrefab;

    [Tooltip("Материал камня. Если null — оставляем материал префаба.")]
    public Material matRock;

    /// <summary>Текущий размер камня (N×N клеток), управляется клавишами 1-5.</summary>
    private int rockSize = 1;

    /// <summary>Математическая плоскость y = 0 для рейкаста мыши.</summary>
    private readonly Plane groundPlane = new Plane(Vector3.up, Vector3.zero);

    /// <summary>Какие камни стоят в каких клетках (нужно для удаления при toggle).</summary>
    private readonly Dictionary<Vector2Int, GameObject> rocks = new Dictionary<Vector2Int, GameObject>();

    /// <summary>Списки заблокированных клеток для каждого камня (ключ = клетка клика).</summary>
    private readonly Dictionary<Vector2Int, List<Vector2Int>> rockCells = new Dictionary<Vector2Int, List<Vector2Int>>();

    /// <summary>Размер камня для каждой ключевой клетки (чтобы знать сколько клеток разблокировать).</summary>
    private readonly Dictionary<Vector2Int, int> rockSizes = new Dictionary<Vector2Int, int>();

    private Camera mainCamera;

    private void Start()
    {
        mainCamera = Camera.main;

        if (mainCamera == null)
        {
            Debug.LogError("[ObstaclePlacer] Камера с тегом MainCamera не найдена.");
        }
    }

    private void Update()
    {
        // Работаем только в режиме «камня»: клавиша B зажата.
        if (!Input.GetKey(KeyCode.B))
        {
            return;
        }

        if (mainCamera == null || GridManager.Instance == null)
        {
            return;
        }

        // Выбор размера камня клавишами 1-5.
        for (int i = 1; i <= 5; i++)
        {
            if (Input.GetKeyDown((KeyCode)(System.Enum.Parse(typeof(KeyCode), "Alpha" + i))))
            {
                rockSize = i;
                Debug.Log($"[ObstaclePlacer] Размер камня: {rockSize}");
                break;
            }
        }

        // Реагируем только на момент нажатия ЛКМ (не на удержание).
        if (!Input.GetMouseButtonDown(0))
        {
            return;
        }

        // Рейкаст луча из мыши на плоскость y = 0 -> точка попадания -> клетка.
        Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);

        if (!groundPlane.Raycast(ray, out float enter))
        {
            return; // луч не попал в плоскость
        }

        Vector3 hitPoint = ray.GetPoint(enter);
        Vector2Int cell = GridManager.Instance.WorldToGrid(hitPoint);

        // Клетка вне границ поля — игнорируем.
        if (!GridManager.Instance.InBounds(cell))
        {
            return;
        }

        // Toggle: клетка заблокирована -> убрать камень и разблокировать;
        // иначе -> поставить камень и заблокировать.
        if (GridManager.Instance.IsBlocked(cell))
        {
            RemoveRock(cell);
        }
        else
        {
            PlaceRock(cell);
        }
    }

    /// <summary>
    /// Поставить камень размером N×N, начиная с клетки cell (нижний левый угол области).
    /// Проверяется доступность ВСЕХ N×N клеток; если хотя бы одна недоступна — отказ.
    /// </summary>
    private void PlaceRock(Vector2Int cell)
    {
        if (rockPrefab == null)
        {
            Debug.LogError("[ObstaclePlacer] rockPrefab не назначен — камень не создан.");
            return;
        }

        int n = rockSize;
        var cells = new List<Vector2Int>(n * n);

        // Собираем все N×N клеток области и проверяем их доступность.
        for (int dx = 0; dx < n; dx++)
        {
            for (int dy = 0; dy < n; dy++)
            {
                Vector2Int c = new Vector2Int(cell.x + dx, cell.y + dy);

                if (!GridManager.Instance.IsFree(c))
                {
                    Debug.LogWarning($"[ObstaclePlacer] Клетка {c} занята/заблокирована/вне границ — постановка камня {n}x{n} отменена.");
                    return;
                }

                cells.Add(c);
            }
        }

        // Блокируем все клетки области.
        foreach (var c in cells)
        {
            GridManager.Instance.SetBlocked(c, true);
        }

        // Визуальное позиционирование: центр области N×N.
        float cellSize = GridManager.Instance.cellSize;
        Vector3 worldPos = GridManager.Instance.GridToWorld(cell) +
                           new Vector3((n - 1) * 0.5f * cellSize, 0f, (n - 1) * 0.5f * cellSize);

        var rock = Instantiate(rockPrefab, worldPos, Quaternion.identity);
        rock.name = $"Rock_{cell.x}_{cell.y}_{n}x{n}";
        float scale = n * 1.0f;
        rock.transform.localScale = new Vector3(scale, scale, scale);

        // Применяем material камню (если назначен) — к первому рендереру в иерархии.
        if (matRock != null)
        {
            var renderer = rock.GetComponentInChildren<Renderer>();

            if (renderer != null)
            {
                renderer.material = matRock;
            }
        }

        rocks[cell] = rock;
        rockCells[cell] = cells;
        rockSizes[cell] = n;

        Debug.Log($"[ObstaclePlacer] Камень {n}x{n} поставлен в клетку {cell}.");
    }

    /// <summary>
    /// Убрать камень по его ключевой клетке: уничтожить GameObject и разблокировать
    /// ВСЕ клетки этого камня.
    /// </summary>
    private void RemoveRock(Vector2Int cell)
    {
        if (rocks.TryGetValue(cell, out var rock))
        {
            Destroy(rock);
            rocks.Remove(cell);
        }

        if (rockCells.TryGetValue(cell, out var cells))
        {
            foreach (var c in cells)
            {
                GridManager.Instance.SetBlocked(c, false);
            }
            rockCells.Remove(cell);
        }

        if (rockSizes.ContainsKey(cell))
        {
            rockSizes.Remove(cell);
        }

        Debug.Log($"[ObstaclePlacer] Камень убран из клетки {cell}.");
    }
}