using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Обрабатывает пользовательский ввод: выделение отряда и растягивание формации.
/// Рейкаст луча из мыши на математическую плоскость y = 0, определение клетки под курсором.
/// Машина состояний: Idle -> Selected (ЛКМ по юниту) -> Dragging (ЛКМ по любой клетке при выделенном отряде).
/// Выделение и drag — РАЗДЕЛЬНЫЕ действия: клик только выделяет, повторное нажатие ЛКМ начинает drag.
/// При отпускании ЛКМ отдается команда перестроения (Squad.CommandFormation).
/// Синглтон — доступ через InputController.Instance. Клавиша T — самотест Formation.SelfTest().
/// Режим камней (зажатая B) перехвачен ObstaclePlacer — обычный ввод в это время отключён.
/// </summary>
public class InputController : MonoBehaviour
{
    public static InputController Instance { get; private set; }

    private enum State { Idle, Selected, Dragging }

    [Header("Visuals")]
    public Material matSelected;
    public Material matUnit;
    public SquadSpawner spawner;

    [Header("Drag Preview")]
    public FormationPreview preview;

    public Squad SelectedSquad { get; private set; }
    private State state = State.Idle;
    private readonly Plane groundPlane = new Plane(Vector3.up, Vector3.zero);
    private Camera mainCamera;

    private Vector2Int dragStartCell;
    private Vector2Int dragEndCell;
    private List<Vector2Int> lastSlots;
    private Vector2Int lastFacing;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }

    private void Start()
    {
        mainCamera = Camera.main;
        if (mainCamera == null) Debug.LogError("[InputController] MainCamera not found.");

        if (matUnit == null && spawner != null) matUnit = spawner.matUnit;
        if (preview == null) preview = FindObjectOfType<FormationPreview>();
    }

    private void Update()
    {
        // ПЕРВОЙ СТРОКОЙ: режим размещения камней (зажатая B) — весь обычный
        // ввод (выделение, drag формации) приостановлен; клики обрабатывает
        // ObstaclePlacer. Это простое разрешение конфликта ввода.
        if (Input.GetKey(KeyCode.B))
        {
            return;
        }

        if (mainCamera == null || GridManager.Instance == null)
        {
            return;
        }

        if (Input.GetKeyDown(KeyCode.T)) Formation.SelfTest();

        // ПКМ - отмена
        if (Input.GetMouseButtonDown(1))
        {
            if (state == State.Dragging)
            {
                // Отмена drag: превью скрывается, отряд остаётся выделенным.
                if (preview != null)
                {
                    preview.Hide();
                }

                lastSlots = null;
                state = State.Selected;
            }
            else if (state == State.Selected)
            {
                SetSelection(null);
                state = State.Idle;
            }
            return;
        }

        // ЛКМ нажата
        if (Input.GetMouseButtonDown(0))
        {
            if (TryGetClampedCellUnderCursor(out Vector2Int cell))
            {
                if (state == State.Selected)
                {
                    // Уже есть выделенный отряд: нажатие на ЛЮБУЮ клетку
                    // (пустую или занятую) начинает drag. DRAG НЕ меняет выделение.
                    BeginDrag(cell);
                }
                else
                {
                    Unit unit = GridManager.Instance.GetOccupant(cell);
                    if (unit != null && unit.Squad != null)
                    {
                        SetSelection(unit.Squad);
                        state = State.Selected;
                    }
                }
            }
        }

        // ЛКМ удерживается
        if (state == State.Dragging && Input.GetMouseButton(0))
        {
            if (TryGetClampedCellUnderCursor(out Vector2Int cell))
            {
                UpdateDrag(cell);
            }
        }

        // --- ЛКМ отпущена: завершаем drag (команда перестроения) ---
        if (state == State.Dragging && Input.GetMouseButtonUp(0))
        {
            EndDrag();
        }
    }

    /// <summary>
    /// Рейкаст луча из позиции мыши на плоскость y = 0 (Plane + Ray.Enter)
    /// и перевод в клетку сетки, ограниченную (clamp) границами поля.
    /// Возвращает false, если луч не попал в плоскость.
    /// </summary>
    private bool TryGetClampedCellUnderCursor(out Vector2Int cell)
    {
        Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
        if (groundPlane.Raycast(ray, out float enter))
        {
            Vector3 hitPoint = ray.GetPoint(enter);
            Vector2Int raw = GridManager.Instance.WorldToGrid(hitPoint);
            int max = GridManager.Instance.size - 1;
            cell = new Vector2Int(Mathf.Clamp(raw.x, 0, max), Mathf.Clamp(raw.y, 0, max));
            return true;
        }
        cell = default;
        return false;
    }

    private void SetSelection(Squad newSquad)
    {
        if (SelectedSquad != null) ApplyMaterial(SelectedSquad, matUnit);
        SelectedSquad = newSquad;
        if (SelectedSquad != null) ApplyMaterial(SelectedSquad, matSelected);
    }

    private void ApplyMaterial(Squad squad, Material mat)
    {
        if (squad == null || mat == null) return;
        foreach (var unit in squad.units) unit?.SetVisual(mat);
    }

    private void BeginDrag(Vector2Int startCell)
    {
        state = State.Dragging;
        dragStartCell = startCell;
        dragEndCell = startCell;
        lastSlots = null;
        lastFacing = Vector2Int.zero;
        UpdateDrag(startCell);
    }

    /// <summary>
    /// Обновление drag: конец линии = клетка под курсором (уже clamp в
    /// TryGetClampedCellUnderCursor), слоты пересчитываются каждый кадр через
    /// Formation.GetLineSlots, превью обновляется.
    /// </summary>
    private void UpdateDrag(Vector2Int endCell)
    {
        if (SelectedSquad == null) return;
        dragEndCell = endCell;
        lastSlots = Formation.GetLineSlots(dragStartCell, dragEndCell, SelectedSquad.units.Count, out lastFacing);
        if (preview != null) preview.Show(lastSlots);
    }

    /// <summary>
    /// Завершение drag (ЛКМ отпущена): приказ выбранному отряду перестроиться в
    /// формацию по посчитанным слотам (CommandFormation резервирует клетки и
    /// запускает движение по A*), скрытие превью, возврат в Selected
    /// (отряд остаётся выделенным).
    /// </summary>
    private void EndDrag()
    {
        // !!! КЛЮЧЕВОЙ МОМЕНТ: Вызываем движение ПЕРЕД скрытием превью !!!
        if (SelectedSquad != null && lastSlots != null && lastSlots.Count > 0)
        {
            SelectedSquad.CommandFormation(lastSlots, lastFacing);
        }

        if (preview != null) preview.Hide();

        Debug.Log($"[InputController] Drag завершён: {dragStartCell} -> {dragEndCell}, слотов: {(lastSlots != null ? lastSlots.Count : 0)}");

        lastSlots = null;
        state = State.Selected;
    }
}