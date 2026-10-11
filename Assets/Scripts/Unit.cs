using UnityEngine;

/// <summary>
/// Юнит на сетке. Хранит логическую позицию, направление взгляда и принадлежность к отряду.
/// Полноценная реализация вместо ранее существовавшей заглушки.
/// </summary>
public class Unit : MonoBehaviour
{
    /// <summary>Индекс клетки сетки, которую занимает юнит.</summary>
    public Vector2Int GridPos;

    /// <summary>Направление взгляда (по умолчанию "вперёд" по оси Y сетки).</summary>
    public Vector2Int Facing = new Vector2Int(0, 1);

    /// <summary>Отряд, к которому принадлежит юнит.</summary>
    public Squad Squad;

    /// <summary>Идет ли сейчас анимация перемещения между клетками.</summary>
    public bool IsMoving;

    /// <summary>Скорость перемещения в клетках в секунду (для плавного Lerp позиции).</summary>
    public float moveSpeed = 4f;

    /// <summary>
    /// Инициализировать юнита: задать позицию и зарезервировать за ним клетку в GridManager.
    /// Если клетка уже занята/недоступна — логируем ошибку и не запускаем логику спавна.
    /// </summary>
    public void Init(Vector2Int cell, Squad squad)
    {
        GridPos = cell;
        Squad = squad;

        if (GridManager.Instance == null)
        {
            Debug.LogError("[Unit] GridManager.Instance == null — невозможно зарегистрировать юнита на сетке.", this);
            return;
        }

        if (!GridManager.Instance.TryOccupy(cell, this))
        {
            Debug.LogError($"[Unit] Клетка {cell} уже занята или недоступна — юнит не заспавнен, логика не запущена.", this);
            return;
        }

        // Позиционируем трансформ в центр клетки.
        transform.position = GridManager.Instance.GridToWorld(cell);

        // Создаём «нос» — child-объект, визуально показывающий направление взгляда.
        CreateNose();

        // Применяем текущее направление: локальный +Z юнита (и «нос» на нём)
        // смотрит вдоль Facing.
        ApplyFacing();

        // Логика появления юнита (анимация спавна, инициализация статов) появится позже.
    }

    /// <summary>Дочерний объект-указатель направления.</summary>
    private Transform noseTransform;

    /// <summary>
    /// Создать child-объект "Nose" КОДОМ (не через префаб): маленький куб,
    /// торчащий вперёд по локальному +Z юнита. Вызывается один раз из Init.
    /// </summary>
    private void CreateNose()
    {
        if (noseTransform != null)
        {
            return; // уже создан
        }

        var nose = GameObject.CreatePrimitive(PrimitiveType.Cube);
        nose.name = "Nose";
        nose.transform.SetParent(transform, false);
        nose.transform.localScale = new Vector3(0.2f, 0.2f, 0.4f);
        // Смещение по +Z: куб выступает за переднюю грань юнита — видно, «куда нос».
        nose.transform.localPosition = new Vector3(0f, 0f, 0.45f);

        noseTransform = nose.transform;
    }

    /// <summary>
    /// Повернуть трансформ юнита так, чтобы его локальный +Z смотрел вдоль
    /// вектора Facing в мировых координатах сетки.
    /// Соответствие клеток: Facing (0,1) -> мир +Z; (1,0) -> мир +X;
    /// (0,-1) -> мир -Z; (-1,0) -> мир -X; диагонали -> 45 градусов.
    ///
    /// ПОЧЕМУ Atan2(Facing.x, Facing.y), а не Atan2(y, x):
    /// стандартный atan2(y, x) измеряет угол от мировой оси +X против часовой.
    /// Нам же нужен угол ВОКРУГ мировой оси Y (yaw в Unity — именно вокруг Y),
    /// то есть угол ОТ направления +Z к направлению +X. В плоскости (X, Z)
    /// «против часовой» для наблюдателя сверху — это движение от +Z к +X,
    /// поэтому роль «синусной» координаты играет X (смещение к +X), а «косинной»
    /// — Z (проекция на +Z). Отсюда atan2(x, z): при Facing=(0,1) угол 0°
    /// (смотреть на +Z), при Facing=(1,0) угол +90° (поворот yaw к +X).
    /// Если бы использовали Atan2(y, x), получили бы угол от +X — юниты
    /// «смотрели» бы на 90° в сторону от реального направления.
    /// </summary>
    public void ApplyFacing()
    {
        float angle = Mathf.Atan2(Facing.x, Facing.y) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0f, angle, 0f);
    }

    /// <summary>
    /// Сменить материал у рендерера юнита (если он есть).
    /// </summary>
    public void SetVisual(Material mat)
    {
        if (mat == null)
        {
            Debug.LogWarning("[Unit] SetVisual: передан null-материал.", this);
            return;
        }

        var renderer = GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.material = mat;
        }
        else
        {
            Debug.LogWarning($"[Unit] На объекте \"{name}\" нет Renderer — материал не применён.", this);
        }
    }

    /// <summary>
    /// Запустить плавное перемещение юнита в целевую клетку (прямая линия,
    /// без обхода препятствий — препятствия добавим позже).
    /// Логическая клетка (GridPos) и резервирование в GridManager уже обновлены
    /// вызывающей стороной (Squad.CommandFormation) ДО запуска этой корутины.
    /// </summary>
    public void StartMove(Vector2Int targetCell)
    {
        if (GridManager.Instance == null)
        {
            Debug.LogError("[Unit] StartMove: GridManager.Instance == null — движение отменено.", this);
            return;
        }

        // Останавливаем предыдущее движение, если оно шло (иначе две корутины
        // будут бороться за transform.position).
        if (moveCoroutine != null)
        {
            StopCoroutine(moveCoroutine);
        }

        moveCoroutine = StartCoroutine(MoveTo(targetCell));
    }

    /// <summary>Активная корутина движения (для остановки при повторном приказе).</summary>
    private Coroutine moveCoroutine;

    /// <summary>
    /// Корутина движения: пошаговое перемещение transform.position к центру
    /// целевой клетки со скоростью moveSpeed клеток в секунду
    /// (Vector3.MoveTowards). Движение прямое, препятствия не учитываются.
    /// По окончании IsMoving = false.
    /// </summary>
    private System.Collections.IEnumerator MoveTo(Vector2Int targetCell)
    {
        Vector3 targetWorldPos = GridManager.Instance.GridToWorld(targetCell);

        IsMoving = true;

        while (Vector3.Distance(transform.position, targetWorldPos) > 0.01f)
        {
            transform.position = Vector3.MoveTowards(
                transform.position,
                targetWorldPos,
                moveSpeed * Time.deltaTime
            );

            yield return null;
        }

        // Финальная точная установка в центр клетки (без остатка в 0.01).
        transform.position = targetWorldPos;
        IsMoving = false;
        moveCoroutine = null;
    }

    private void OnDestroy()
    {
        // При уничтожении освобождаем клетку, чтобы она снова стала доступной.
        if (GridManager.Instance != null)
        {
            GridManager.Instance.Free(GridPos, this);
        }
    }
}
