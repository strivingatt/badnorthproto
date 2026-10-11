using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Инструмент размещения препятствий: пока зажата клавиша B, клик ЛКМ по клетке
/// ставит/убирает камень (блокирует/разблокирует клетку в GridManager).
/// Конфликт с обычным вводом решён в InputController.Update первой строкой:
/// if (Input.GetKey(KeyCode.B)) return; — пока активен режим камней, выделение
/// и drag формации не обрабатываются.
/// </summary>
public class ObstaclePlacer : MonoBehaviour
{
    [Header("Obstacles")]
    [Tooltip("Префаб камня-препятствия (обычный 3D-объект с Renderer).")]
    public GameObject rockPrefab;

    [Tooltip("Материал камня. Если null — оставляем материал префаба.")]
    public Material matRock;

    /// <summary>Математическая плоскость y = 0 для рейкаста мыши.</summary>
    private readonly Plane groundPlane = new Plane(Vector3.up, Vector3.zero);

    /// <summary>Какие камни стоят в каких клетках (для удаления по toggle).</summary>
    private readonly Dictionary<Vector2Int, GameObject> rocks = new Dictionary<Vector2Int, GameObject>();

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

        if (!Input.GetMouseButtonDown(0))
        {
            return;
        }

        // Рейкаст луча из мыши на плоскость y = 0 -> клетка под курсором.
        Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);

        if (!groundPlane.Raycast(ray, out float enter))
        {
            return; // луч не попал в плоскость
        }

        Vector3 hitPoint = ray.GetPoint(enter);
        Vector2Int cell = GridManager.Instance.WorldToGrid(hitPoint);

        // Клетка вне границ — игнорируем.
        if (!GridManager.Instance.InBounds(cell))
        {
            return;
        }

        // Если клетку занимает юнит — игнорируем (не ставим камень на юнита).
        if (GridManager.Instance.GetOccupant(cell) != null)
        {
            Debug.Log($"[ObstaclePlacer] Клетка {cell} занята юнитом — камень не поставлен.");
            return;
        }

        // Toggle: заблокирована -> убрать камень и разблокировать; иначе поставить.
        if (GridManager.Instance.IsBlocked(cell))
        {
            if (rocks.TryGetValue(cell, out var rock))
            {
                Destroy(rock);
                rocks.Remove(cell);
            }

            GridManager.Instance.SetBlocked(cell, false);
            Debug.Log($"[ObstaclePlacer] Камень убран из клетки {cell}.");
        }
        else
        {
            if (rockPrefab == null)
            {
                Debug.LogError("[ObstaclePlacer] rockPrefab не назначен — камень не создан.");
                return;
            }

            Vector3 worldPos = GridManager.Instance.GridToWorld(cell);
            var rock = Instantiate(rockPrefab, worldPos, Quaternion.identity);
            rock.name = $"Rock_{cell.x}_{cell.y}";
            rock.transform.localScale = new Vector3(0.9f, 0.9f, 0.9f);

            if (matRock != null)
            {
                var renderer = rock.GetComponentInChildren<Renderer>();

                if (renderer != null)
                {
                    renderer.material = matRock;
                }
            }

            rocks[cell] = rock;
            GridManager.Instance.SetBlocked(cell, true);
            Debug.Log($"[ObstaclePlacer] Камень поставлен в клетку {cell}.");
        }
    }
}
