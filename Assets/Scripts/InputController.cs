using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Обрабатывает пользовательский ввод: выделение отряда и растягивание формации.
/// Рейкаст луча из мыши на математическую плоскость y = 0, определение клетки под курсором.
/// Машина состояний: Idle -> Selected (ЛКМ по юниту) -> Dragging (ЛКМ по любой клетке при выделенном отряде).
/// Выделение и drag — РАЗДЕЛЬНЫЕ действия: клик только выделяет, повторное нажатие ЛКМ начинает drag.
/// Синглтон — доступ через InputController.Instance. Клавиша T — самотест Formation.SelfTest().
/// </summary>
public class InputController : MonoBehaviour
{
    public static InputController Instance { get; private set; }

    /// <summary>Состояния машины ввода.</summary>
    private enum State
    {
        /// <summary>Ничего не выбрано.</summary>
        Idle,
        /// <summary>Отряд выбран, ждём начала drag.</summary>
        Selected,
        /// <summary>Идёт растягивание формации (ЛКМ зажата).</summary>
        Dragging,
    }

    [Header("Visuals")]
    [Tooltip("Материал для подсветки выбранного отряда.")]
    public Material matSelected;

    [Tooltip("Обычный материал юнитов. Если не назначен, берётся из SquadSpawner.matUnit.")]
    public Material matUnit;

    [Tooltip("Спавнер, у которого можно взять базовый материал юнитов (необязательно).")]
    public SquadSpawner spawner;

    [Header("Drag Preview")]
    [Tooltip("Компонент превью формации. Если не назначен — ищется на сцене.")]
    public FormationPreview preview;

    /// <summary>Отряд, выбранный в данный момент (null — ничего не выбрано).</summary>
    public Squad SelectedSquad { get; private set; }

    /// <summary>Текущее состояние машины ввода.</summary>
    private State state = State.Idle;

    /// <summary>Математическая плоскость y = 0 для рейкаста мыши.</summary>
    private readonly Plane groundPlane = new Plane(Vector3.up, Vector3.zero);

    private Camera mainCamera;

    // Состояние drag.
    private Vector2Int dragStartCell;
    private Vector2Int dragEndCell;
    private List<Vector2Int> lastSlots;
    private Vector2Int lastFacing;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[InputController] Дубликат экземпляра удалён.", this);
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

    private void Start()
    {
        mainCamera = Camera.main;

        if (mainCamera == null)
        {
            Debug.LogError("[InputController] Камера с тегом MainCamera не найдена.");
        }

        // Если matUnit не назначен вручную — берём его из поля спавнера.
        if (matUnit == null)
        {
            if (spawner == null)
            {
                spawner = FindObjectOfType<SquadSpawner>();
            }

            if (spawner != null)
            {
                matUnit = spawner.matUnit;
            }
        }

        // Если preview не назначен — ищем компонент на сцене.
        if (preview == null)
        {
            preview = FindObjectOfType<FormationPreview>();
        }
    }

    private void Update()
    {
        if (mainCamera == null || GridManager.Instance == null)
        {
            return;
        }

        // Клавиша T — самотест построения формаций.
        if (Input.GetKeyDown(KeyCode.T))
        {
            Formation.SelfTest();
        }

        // --- ПКМ ---
        if (Input.GetMouseButtonDown(1))
        {
            if (state == State.Dragging)
            {
                // Отмена drag: превью скрывается, отряд остаётся выделенным.
                if (preview != null)
                {
                    preview.Hide();
                }

                state = State.Selected;
                Debug.Log("[InputController] Drag отменён (ПКМ).");
            }
            else if (state == State.Selected)
            {
                // Снятие выделения.
                SetSelection(null);
                state = State.Idle;
            }

            return;
        }

        // --- ЛКМ нажата ---
        if (Input.GetMouseButtonDown(0))
        {
            Vector2Int cell;
            bool ok = TryGetClampedCellUnderCursor(out cell);

            if (!ok)
            {
                return; // луч не попал в плоскость
            }

            if (state == State.Idle || state == State.Selected)
            {
                if (state == State.Selected)
                {
                    // Уже есть выделенный отряд: нажатие на ЛЮБУЮ клетку начинает drag.
                    // DRAG НЕ меняет выделение.
                    BeginDrag(cell);
                }
                else
                {
                    // Idle: клик по юниту выделяет его отряд. Drag НЕ начинается,
                    // превью в этот кадр не создаётся.
                    Unit unit = GridManager.Instance.GetOccupant(cell);

                    if (unit != null && unit.Squad != null)
                    {
                        SetSelection(unit.Squad);
                        state = State.Selected;
                    }
                }
            }
        }

        // --- ЛКМ удерживается: обновляем превью ---
        if (state == State.Dragging && Input.GetMouseButton(0))
        {
            Vector2Int cell;

            // Если луч не попал в плоскость — пропускаем кадр.
            if (TryGetClampedCellUnderCursor(out cell))
            {
                UpdateDrag(cell);
            }
        }

        // --- ЛКМ отпущена: завершаем drag ---
        if (state == State.Dragging && Input.GetMouseButtonUp(0))
        {
            EndDrag();
        }
    }

    /// <summary>
    /// Рейкаст луча из позиции мыши на плоскость y = 0 и перевод в клетку сетки,
    /// ограниченную (clamp) границами поля. Возвращает false, если луч не попал в плоскость.
    /// </summary>
    private bool TryGetClampedCellUnderCursor(out Vector2Int cell)
    {
        Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);

        if (groundPlane.Raycast(ray, out float enter))
        {
            Vector3 hitPoint = ray.GetPoint(enter);
            Vector2Int raw = GridManager.Instance.WorldToGrid(hitPoint);
            int max = GridManager.Instance.size - 1;

            cell = new Vector2Int(
                Mathf.Clamp(raw.x, 0, max),
                Mathf.Clamp(raw.y, 0, max)
            );

            return true;
        }

        cell = default;
        return false;
    }

    /// <summary>
    /// Смена выделения: старый отряд возвращается к matUnit, новый подсвечивается matSelected.
    /// </summary>
    private void SetSelection(Squad newSquad)
    {
        if (SelectedSquad != null)
        {
            ApplyMaterial(SelectedSquad, matUnit);
        }

        SelectedSquad = newSquad;

        if (SelectedSquad != null)
        {
            ApplyMaterial(SelectedSquad, matSelected);
        }
    }

    /// <summary>Применить материал ко всем юнитам отряда (null-материал пропускается).</summary>
    private void ApplyMaterial(Squad squad, Material mat)
    {
        if (squad == null || mat == null)
        {
            return;
        }

        foreach (var unit in squad.units)
        {
            if (unit != null)
            {
                unit.SetVisual(mat);
            }
        }
    }

    // ------------------------------------------------------------------
    // Drag формации (отдельное действие после выделения):
    // ------------------------------------------------------------------

    /// <summary>Начало drag: фиксируем стартовую клетку и сразу показываем превью.</summary>
    private void BeginDrag(Vector2Int startCell)
    {
        state = State.Dragging;
        dragStartCell = startCell;
        dragEndCell = startCell;
        lastSlots = null;
        lastFacing = Vector2Int.zero;

        // Сразу пересчитываем и показываем превью (линия нулевой длины).
        UpdateDrag(startCell);
    }

    /// <summary>
    /// Обновление drag: конец линии = клетка под курсором (уже clamp в TryGetClampedCellUnderCursor),
    /// слоты пересчитываются каждый кадр, превью обновляется.
    /// </summary>
    private void UpdateDrag(Vector2Int endCell)
    {
        if (SelectedSquad == null)
        {
            return;
        }

        dragEndCell = endCell;

        int count = SelectedSquad.units.Count;
        List<Vector2Int> slots = Formation.GetLineSlots(dragStartCell, dragEndCell, count, out Vector2Int facing);

        lastSlots = slots;
        lastFacing = facing;

        if (preview != null)
        {
            preview.Show(slots);
        }
    }

    /// <summary>
    /// Завершение drag (ЛКМ отпущена): лог списка слотов и facing, Hide(), возврат в Selected.
    /// TODO (следующий шаг): валидация целевых клеток и приказ на движение отряду.
    /// </summary>
    private void EndDrag()
    {
        if (preview != null)
        {
            preview.Hide();
        }

        Debug.Log($"[InputController] Drag завершён: {dragStartCell} -> {dragEndCell}, " +
                  $"facing = {lastFacing}, слотов ({(lastSlots != null ? lastSlots.Count : 0)}): " +
                  $"{(lastSlots != null ? string.Join(", ", lastSlots) : "-")}.");

        lastSlots = null;
        state = State.Selected; // отряд остаётся выделенным
    }
}
