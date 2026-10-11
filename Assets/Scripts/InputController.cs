using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Обрабатывает пользовательский ввод: выбор отряда кликом мыши по сетке.
/// Рейкаст луча из мыши на математическую плоскость y = 0, определение клетки под курсором,
/// выделение отряда юнита, находящегося в этой клетке, и подсветка выбранного отряда.
/// Синглтон — доступ через InputController.Instance.
/// Drag (перетаскивание отряда): ЛКМ зажато над выбранным отрядом — превью формации «шеренги»
/// через Formation.GetLineSlots и FormationPreview; ПКМ отменяет drag.
/// </summary>
public class InputController : MonoBehaviour
{
    public static InputController Instance { get; private set; }

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

    /// <summary>Математическая плоскость y = 0 для рейкаста мыши.</summary>
    private readonly Plane groundPlane = new Plane(Vector3.up, Vector3.zero);

    private Camera mainCamera;

    // Состояние drag.
    private bool isDragging;
    private Vector2Int dragStartCell;
    private Vector2Int dragCurrentCell;
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

        // ПКМ — отмена drag и скрытие превью.
        if (Input.GetMouseButtonDown(1))
        {
            CancelDrag();
            return;
        }

        if (Input.GetMouseButtonDown(0))
        {
            HandleClick();

            // Клик по уже выбранному отряду — начинаем drag от этой клетки.
            if (SelectedSquad != null)
            {
                Vector2Int cell = GetCellUnderCursor(out bool ok);

                if (ok)
                {
                    Unit unit = GridManager.Instance.GetOccupant(cell);

                    if (unit != null && unit.Squad == SelectedSquad)
                    {
                        OnDragStart(cell);
                    }
                }
            }
        }

        // Пока selectedSquad != null и ЛКМ зажата — обновляем превью формации.
        if (isDragging && Input.GetMouseButton(0))
        {
            Vector2Int cell = GetCellUnderCursor(out bool ok);

            if (ok)
            {
                OnDragUpdate(cell);
            }
        }

        // ЛКМ отпустили — завершаем drag.
        if (isDragging && Input.GetMouseButtonUp(0))
        {
            OnDragEnd();
        }
    }

    /// <summary>Клик ЛКМ: определяем клетку под курсором и выделяем отряд юнита в ней.</summary>
    private void HandleClick()
    {
        if (mainCamera == null || GridManager.Instance == null)
        {
            return;
        }

        Vector2Int cell = GetCellUnderCursor(out bool ok);
        if (!ok)
        {
            return;
        }

        Unit unit = GridManager.Instance.GetOccupant(cell);

        // Меняем ли мы выделение?
        Squad newSquad = unit != null ? unit.Squad : null;

        if (newSquad == SelectedSquad)
        {
            return; // выделение не изменилось — перекраску не делаем
        }

        SetSelection(newSquad);
    }

    /// <summary>
    /// Рейкаст луча из позиции мыши на плоскость y = 0.
    /// Возвращает клетку сетки под курсором; ok = false, если луч не попал в плоскость.
    /// </summary>
    private Vector2Int GetCellUnderCursor(out bool ok)
    {
        Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);

        if (groundPlane.Raycast(ray, out float enter))
        {
            Vector3 hitPoint = ray.GetPoint(enter);
            ok = true;
            return GridManager.Instance.WorldToGrid(hitPoint);
        }

        ok = false;
        return default;
    }

    /// <summary>
    /// Смена выделения: старый отряд возвращается к matUnit, новый подсвечивается matSelected.
    /// </summary>
    private void SetSelection(Squad newSquad)
    {
        // Снимаем подсветку со старого отряда.
        if (SelectedSquad != null)
        {
            ApplyMaterial(SelectedSquad, matUnit);
        }

        SelectedSquad = newSquad;

        // Подсвечиваем новый отряд.
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
    // Drag выбранного отряда (превью формации):
    // ------------------------------------------------------------------

    /// <summary>Начало перетаскивания: фиксируем стартовую клетку под курсором.</summary>
    private void OnDragStart(Vector2Int startCell)
    {
        isDragging = true;
        dragStartCell = startCell;
        dragCurrentCell = startCell;
        lastSlots = null;
        lastFacing = Vector2Int.zero;
    }

    /// <summary>
    /// Обновление перетаскивания: каждый кадр пересчитываем слоты формации
    /// «шеренги» от стартовой клетки до текущей и показываем превью.
    /// </summary>
    private void OnDragUpdate(Vector2Int currentCell)
    {
        if (SelectedSquad == null)
        {
            return;
        }

        dragCurrentCell = currentCell;

        int count = SelectedSquad.units.Count;
        List<Vector2Int> slots = Formation.GetLineSlots(dragStartCell, dragCurrentCell, count, out Vector2Int facing);

        lastSlots = slots;
        lastFacing = facing;

        if (preview != null)
        {
            preview.Show(slots);
        }
    }

    /// <summary>
    /// Завершение перетаскивания (ЛКМ отпущена): пока только скрываем превью и логируем слоты.
    /// TODO (следующий шаг): валидация целевых клеток и приказ на движение отряду.
    /// </summary>
    private void OnDragEnd()
    {
        isDragging = false;

        if (preview != null)
        {
            preview.Hide();
        }

        Debug.Log($"[InputController] Drag завершён: {dragStartCell} -> {dragCurrentCell}, " +
                  $"facing = {lastFacing}, слотов ({(lastSlots != null ? lastSlots.Count : 0)}): " +
                  $"{(lastSlots != null ? string.Join(", ", lastSlots) : "-")}.");

        lastSlots = null;
    }

    /// <summary>Отмена перетаскивания (ПКМ): сбрасываем состояние и скрываем превью.</summary>
    private void CancelDrag()
    {
        if (!isDragging)
        {
            return;
        }

        isDragging = false;
        lastSlots = null;

        if (preview != null)
        {
            preview.Hide();
        }

        Debug.Log("[InputController] Drag отменён (ПКМ).");
    }
}
