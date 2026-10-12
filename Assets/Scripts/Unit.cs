using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Юнит на сетке. Хранит логическую позицию, направление взгляда и принадлежность
/// к отряду. Движение — по клеткам пути A* (обход заблокированных клеток).
/// </summary>
public class Unit : MonoBehaviour
{
    /// <summary>Индекс клетки сетки, которую занимает юнит.</summary>
    public Vector2Int GridPos;

    /// <summary>Направление взгляда (по умолчанию «вперёд» по оси Y сетки = мир +Z).</summary>
    public Vector2Int Facing = new Vector2Int(0, 1);

    /// <summary>Отряд, к которому принадлежит юнит.</summary>
    public Squad Squad;

    /// <summary>Идет ли сейчас движение между клетками.</summary>
    public bool IsMoving;

    /// <summary>Скорость перемещения в клетках в секунду (Vector3.MoveTowards по клеткам пути A*).</summary>
    public float moveSpeed = 4f;

    // --- Боевые характеристики (Шаг 8) ---

    /// <summary>Команда юнита.</summary>
    public Team team = Team.Player;

    /// <summary>Максимальное здоровье.</summary>
    public int maxHp = 3;

    /// <summary>Текущее здоровье (только чтение извне).</summary>
    private int hp;
    public int Hp => hp;

    /// <summary>Урон одной атаки.</summary>
    public int attackDamage = 1;

    /// <summary>Длительная перезарядки атаки, секунд.</summary>
    public float attackCooldown = 0.8f;

    /// <summary>Оставшееся время до следующей атаки, секунд (тикает CombatResolver).</summary>
    public float cooldownTimer = 0f;

    /// <summary>Материал вспышки при получении урона (назначается спавнером).</summary>
    public Material matHit;

    /// <summary>Активная корутина вспышки «получил урон».</summary>
    private Coroutine flashCoroutine;

    /// <summary>Активная корутина движения (чтобы останавливать при повторном приказе).</summary>
    private Coroutine moveCoroutine;

    /// <summary>Дочерний объект-указатель направления («нос»).</summary>
    private Transform noseTransform;

    /// <summary>Последняя зафиксированная клетка (для синхронизации GridPos без физики).</summary>
    private Vector2Int lastGridPos;

    /// <summary>«Родной» материал юнита (белый у игроков, красный у врагов):
    /// запоминается при спавне, восстанавливается после снятия выделения.</summary>
    private Material defaultMaterial;

    /// <summary>
    /// Инициализировать юнита: задать позицию и зарезервировать за ним клетку
    /// в GridManager. Если клетка уже занята/недоступна — логируем ошибку и
    /// НЕ запускаем логику спавна.
    /// </summary>
    public void Init(Vector2Int cell, Squad squad)
    {
        GridPos = cell;
        Squad = squad;
        hp = maxHp;

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

        // Фиксируем стартовую клетку для синхронизации позиции в FixedUpdate.
        lastGridPos = cell;

        // Создаём «нос» child-объектом КОДОМ (не через префаб) — визуальный
        // указатель направления, торчит вперёд по локальному +Z.
        CreateNose();

        // Применяем текущее направление: локальный +Z юнита смотрит вдоль Facing.
        ApplyFacing();

        // ИИ врагов: если это вражеский юнит — навешиваем EnemyAI кодом
        // (компонент появляется автоматически сразу после регистрации на сетке).
        if (team == Team.Enemy && GetComponent<EnemyAI>() == null)
        {
            gameObject.AddComponent<EnemyAI>();
        }
    }

    /// <summary>
    /// Синхронизация логической позиции с фактической (БЕЗ физики): каждый
    /// FixedUpdate читаем клетку под transform.position и, если юнит вошёл в
    /// новую клетку, обновляем GridPos и occupancy. Занятая чужим юнитом клетка
    /// — логическая коллизия: позиция откатывается к последней зафиксированной.
    /// </summary>
    private void FixedUpdate()
    {
        if (GridManager.Instance == null) return;

        Vector2Int currentCell = GridManager.Instance.WorldToGrid(transform.position);

        // Deadzone: не фиксируем смену клетки, пока юнит не подъехал
        // достаточно близко к её центру.
        Vector3 currentWorldPos = GridManager.Instance.GridToWorld(currentCell);
        float distToCenter = Vector3.Distance(transform.position, currentWorldPos);
        if (distToCenter > 0.4f) return;

        if (currentCell != GridPos && GridManager.Instance.InBounds(currentCell))
        {
            GridManager.Instance.Free(GridPos, this);
            GridManager.Instance.ForceOccupy(currentCell, this); // перезаписываем, кого там ни было
            GridPos = currentCell;
        }
    }

    /// <summary>
    /// Создать child-объект "Nose" кодом (не префабом): маленький куб
    /// localScale (0.2, 0.2, 0.4), localPosition (0, 0, 0.45) — выступает
    /// вперёд по локальному +Z юнита. Родитель — трансформ юнита.
    /// Вызывается один раз из Init.
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
        nose.transform.localPosition = new Vector3(0f, 0f, 0.45f);

        noseTransform = nose.transform;
    }

    /// <summary>
    /// Повернуть трансформ юнита так, чтобы его локальный +Z смотрел вдоль
    /// вектора Facing в мировых координатах.
    /// Соответствие: Facing (0,1) -> мир +Z; (1,0) -> мир +X; (0,-1) -> мир -Z;
    /// (-1,0) -> мир -X; диагонали -> 45 градусов.
    ///
    /// ПОЧЕМУ Atan2(Facing.x, Facing.y), а не Atan2(y, x):
    /// стандартный atan2(y, x) измеряет угол ОТ мировой оси +X против часовой,
    /// но в Unity yaw — это угол ВОКРУГ оси Y, отсчитываемый ОТ направления +Z
    /// к направлению +X. Поэтому роль «синусной» координаты играет X, а
    /// «косинной» — Z. Atan2(y, x) дал бы разворот на 90° относительно нужного.
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
    /// Назначить «родной» материал юнита и сразу применить его.
    /// Вызывается при спавне (SquadSpawner) вместо SetVisual — чтобы юнит
    /// помнил свой исходный цвет и мог восстановить его после снятия выделения.
    /// </summary>
    public void SetDefaultVisual(Material mat)
    {
        defaultMaterial = mat;
        SetVisual(mat);
    }

    /// <summary>
    /// Восстановить «родной» материал (снять подсветку выделения).
    /// Если родной материал не задан — ничего не делаем.
    /// </summary>
    public void RestoreDefaultVisual()
    {
        if (defaultMaterial != null)
        {
            SetVisual(defaultMaterial);
        }
    }

    /// <summary>
    /// Клетки зоны атаки: 3 клетки фронта прямо перед юнитом
    /// (центр — GridPos + Facing, плюс ±1 по перпендикуляру «вправо»).
    ///
    /// Пример расчёта для Facing=(0,1): right = (Facing.y, -Facing.x) = (1,0);
    /// клетки: (x+1,y-1), (x+1,y), (x+1,y+1) — столбец前方 на y+1 шириной 3.
    /// </summary>
    public List<Vector2Int> GetAttackCells()
    {
        Vector2Int right = new Vector2Int(Facing.y, -Facing.x); // перпендикуляр вправо
        List<Vector2Int> cells = new List<Vector2Int>();

        for (int r = -1; r <= 1; r++)
        {
            Vector2Int cell = GridPos + Facing * 1 + right * r;
            cells.Add(cell);
        }

        return cells;
    }

    /// <summary>
    /// Попытка атаки: цели — активные враги команды в зоне атаки.
    /// Кулдаун запускается только при наличии целей.
    /// </summary>
    public bool TryAttack()
    {
        var gm = GridManager.Instance;
        if (gm == null) return false;

        List<Unit> targets = new List<Unit>();

        foreach (Vector2Int cell in GetAttackCells())
        {
            if (!gm.InBounds(cell)) continue;

            Unit occupant = gm.GetOccupant(cell);

            // Проверяем, что враг активен (not destroyed) — иначе мёртвый юнит
            // мог формально оставаться в клетке.
            if (occupant != null && occupant.team != this.team && occupant.gameObject != null)
            {
                targets.Add(occupant);
            }
        }

        if (targets.Count == 0) return false;

        cooldownTimer = attackCooldown;
        Debug.Log($"[Combat] {name} бьёт {targets.Count} целей на {attackDamage} урона");

        foreach (var target in targets)
        {
            target.TakeDamage(attackDamage, this);
        }

        return true;
    }

    /// <summary>
    /// Получить урон: минус HP, вспышка материала, смерть при HP <= 0.
    /// </summary>
    public void TakeDamage(int dmg, Unit attacker)
    {
        hp -= dmg;
        Debug.Log($"[Combat] {name} получил {dmg} урона (HP: {hp}/{maxHp})");

        if (flashCoroutine != null) StopCoroutine(flashCoroutine);
        flashCoroutine = StartCoroutine(Flash());

        if (hp <= 0)
        {
            Die();
        }
    }

    /// <summary>
    /// Визуальная вспышка «получил урон»: подмена материала на matHit на 0.1 сек.
    /// </summary>
    private IEnumerator Flash()
    {
        var renderer = GetComponent<Renderer>();
        if (renderer == null) yield break;

        Material original = renderer.material;
        renderer.material = matHit;
        yield return new WaitForSeconds(0.1f);

        if (this != null && renderer != null)
        {
            renderer.material = original;
        }

        flashCoroutine = null;
    }

    /// <summary>
    /// Смерть юнита: освободить клетку, убрать себя из отряда, уничтожить объект.
    /// </summary>
    public void Die()
    {
        Debug.Log($"[Combat] {name} погиб");

        if (GridManager.Instance != null)
        {
            GridManager.Instance.FreeUnit(this);
        }

        if (Squad != null)
        {
            Squad.Remove(this);
        }

        Destroy(gameObject);
    }

    /// <summary>
    /// Запустить перемещение юнита из клетки fromCell в целевую клетку targetCell
    /// ПО ПУТИ A* (обход камней). Стартовая клетка передаётся ЯВНО: GridPos уже
    /// может быть равен targetCell (резервирование), брать его как старт нельзя.
    /// </summary>
    public void StartMove(Vector2Int fromCell, Vector2Int targetCell)
    {
        if (GridManager.Instance == null)
        {
            Debug.LogError("[Unit] StartMove: GridManager.Instance == null — движение отменено.", this);
            return;
        }

        List<Vector2Int> path = Pathfinding.AStar(fromCell, targetCell);

        if (path == null)
        {
            Debug.LogWarning($"[Unit] StartMove: путь недоступен ({fromCell} -> {targetCell}) — " +
                             $"юнит {name} остаётся на месте.", this);
            IsMoving = false;
            return;
        }

        // Останавливаем предыдущее движение, если оно шло.
        if (moveCoroutine != null)
        {
            StopCoroutine(moveCoroutine);
        }

        // Фиксируем реальную стартовую клетку для синхронизации в FixedUpdate.
        lastGridPos = fromCell;

        moveCoroutine = StartCoroutine(MoveAlongPath(path, targetCell));
    }

    /// <summary>
    /// Корутина движения ПО КЛЕТКАМ пути: MoveTowards к центру каждой клетки
    /// пути со скоростью moveSpeed. Изменяется ТОЛЬКО transform.position —
    /// rotation (Facing, «нос») не трогается. GridPos ведёт FixedUpdate.
    /// </summary>
    private System.Collections.IEnumerator MoveAlongPath(List<Vector2Int> path, Vector2Int targetCell)
    {
        IsMoving = true;

        for (int i = 0; i < path.Count; i++)
        {
            Vector3 waypointPos = GridManager.Instance.GridToWorld(path[i]);

            while (Vector3.Distance(transform.position, waypointPos) > 0.01f)
            {
                transform.position = Vector3.MoveTowards(
                    transform.position,
                    waypointPos,
                    moveSpeed * Time.deltaTime
                );

                yield return null;
            }
        }

        // Финальная доводка в центр целевой клетки.
        transform.position = GridManager.Instance.GridToWorld(targetCell);

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