using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Юнит на сетке. Хранит логическую позицию, направление взгляда и принадлежность
/// к отряду. Движение — по клеткам пути A* (обход заблокированных клеток).
///
/// ВАЖНО: юниты НЕ блокируют друг друга. Клетка в occupancy просто
/// перезаписывается последним вошедшим — юниты проходят сквозь друг друга,
/// никаких откатов, ожиданий и дёргания.
/// </summary>
public class Unit : MonoBehaviour
{
    /// <summary>Индекс клетки сетки, которую юнит занимает ФАКТИЧЕСКИ (обновляется в FixedUpdate).</summary>
    public Vector2Int GridPos;

    /// <summary>Направление взгляда (по умолчанию «вперёд» по оси Y сетки = мир +Z).</summary>
    public Vector2Int Facing = new Vector2Int(0, 1);

    /// <summary>Отряд, к которому принадлежит юнит.</summary>
    public Squad Squad;

    /// <summary>Идет ли сейчас движение между клетками.</summary>
    public bool IsMoving;

    /// <summary>Скорость перемещения в клетках в секунду (Vector3.MoveTowards по клеткам пути A*).</summary>
    public float moveSpeed = 4f;

    // ------------------------------------------------------------------
    // Шаг 8: боевая система MVP.
    // ------------------------------------------------------------------

    /// <summary>Команда юнита (Player/Enemy). Атаки проходят только по чужой команде.</summary>
    public Team team = Team.Player;

    /// <summary>Максимальное здоровье (назначается до AddComponent/Instantiate).</summary>
    public int maxHp = 3;

    /// <summary>Текущее здоровье. Инициализируется из maxHp в Init().</summary>
    private int hp;

    /// <summary>Текущее HP для внешних систем (CombatResolver, UI в будущем).</summary>
    public int Hp => hp;

    /// <summary>Урон одного удара.</summary>
    public int attackDamage = 1;

    /// <summary>Длительность перезарядки атаки, секунд.</summary>
    public float attackCooldown = 0.8f;

    /// <summary>Оставшееся время перезарядки (тикает CombatResolver).</summary>
    public float cooldownTimer = 0f;

    /// <summary>Материал «вспышки» при получении урона (назначается спавнером).</summary>
    public Material matHit;

    /// <summary>Активная корутина вспышки (чтобы не наслаивать эффекты).</summary>
    private Coroutine flashCoroutine;

    /// <summary>Активная корутина движения (чтобы останавливать при повторном приказе).</summary>
    private Coroutine moveCoroutine;

    /// <summary>Дочерний объект-указатель направления («нос»).</summary>
    private Transform noseTransform;

    /// <summary>Последняя клетка, зафиксированная в occupancy — точка отката, если новая клетка занята.</summary>
    private Vector2Int lastGridPos;

    /// <summary>
    /// Инициализировать юнита: задать позицию и зарезервировать за ним клетку
    /// в GridManager. Если клетка уже занята/недоступна — логируем ошибку и
    /// НЕ запускаем логику спавна.
    /// </summary>
    public void Init(Vector2Int cell, Squad squad)
    {
        GridPos = cell;
        Squad = squad;

        // Шаг 8: инициализация боя — hp стартует с максимума.
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

        // Создаём «нос» child-объектом КОДОМ (не через префаб) — визуальный
        // указатель направления, торчит вперёд по локальному +Z.
        CreateNose();

        // Применяем текущее направление: локальный +Z юнита смотрит вдоль Facing.
        ApplyFacing();

        // Логическая позиция = фактическая: стартуем с клетки спавна.
        // (Физика не используется — никаких Rigidbody/Collider, защита от
        // прохода сквозь юнитов держится на откате позиции в FixedUpdate.)
        lastGridPos = cell;

        // ИИ врагов: если это вражеский юнит — навешиваем EnemyAI кодом
        // (префаб не трогаем). Компонент сам найдёт и преследует ближайшего
        // игрока; атаку выполняет CombatResolver тиком TryAttack().
        if (team == Team.Enemy && GetComponent<EnemyAI>() == null)
        {
            gameObject.AddComponent<EnemyAI>();
        }
    }

    /// <summary>
    /// Синхронизация логической позиции с фактической (FixedUpdate).
    ///
    /// Юниты НЕ блокируют друг друга: юнит всегда занимает ту клетку, куда
    /// фактически доехал, перезаписывая occupancy. Никаких откатов.
    ///
    /// Deadzone 0.4: смена клетки фиксируется только когда юнит подошёл к её
    /// центру ближе чем на 0.4 (клетка = 1.0, центр = 0.5) — защита от дёрганья
    /// на границах клеток.
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
        if (noseTransform != null) return;

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
    /// Запустить перемещение юнита из клетки fromCell в целевую клетку targetCell
    /// ПО ПУТИ A* (обход камней). Юниты друг друга не блокируют.
    ///
    /// Если путь не найден — warning «путь недоступен», IsMoving = false,
    /// юнит остаётся на месте.
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

        // Точка отката при «логической коллизии» = фактическая стартовая клетка приказа.
        lastGridPos = fromCell;

        moveCoroutine = StartCoroutine(MoveAlongPath(path, targetCell));
    }

    /// <summary>
    /// Корутина движения ПО КЛЕТКАМ пути: для каждой клетки пути последовательно
    /// MoveTowards к GridToWorld(cell) со скоростью moveSpeed клеток в секунду,
    /// затем следующая клетка. Изменяется ТОЛЬКО transform.position.
    ///
    /// Юниты не блокируют друг друга — никаких ожиданий и перестроений.
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

        // Финальная доводка в центр целевой клетки (только позиция — GridPos
        // зафиксирует этот центр на следующем шаге FixedUpdate).
        transform.position = GridManager.Instance.GridToWorld(targetCell);

        IsMoving = false;
        moveCoroutine = null;
    }

    private void OnDestroy()
    {
        // При уничтожении освобождаем клетку, чтобы она снова стала доступной.
        // Free идемпотентен (проверяет occupant == unit).
        if (GridManager.Instance != null)
        {
            GridManager.Instance.Free(GridPos, this);
        }
    }

    // ------------------------------------------------------------------
    // Шаг 8: боевая система MVP — зона атаки, удары, урон, вспышка, смерть.
    // ------------------------------------------------------------------

    /// <summary>
    /// Зона атаки: 3 клетки перед юнитом («фронт» шириной 3).
    /// right — перпендикуляр Facing «вправо»: right = (Facing.y, -Facing.x).
    /// Клетки: GridPos + Facing*1 + right*r, r = -1..1.
    /// </summary>
    public List<Vector2Int> GetAttackCells()
    {
        Vector2Int right = new Vector2Int(Facing.y, -Facing.x);
        List<Vector2Int> cells = new List<Vector2Int>();

        for (int r = -1; r <= 1; r++)
        {
            Vector2Int cell = GridPos + Facing * 1 + right * r;
            cells.Add(cell);
        }

        return cells;
    }

    /// <summary>
    /// Попытка атаки: наносит attackDamage всем вражеским юнитам в зоне атаки.
    /// Юнит бьёт и двигаясь (GridPos = фактическая клетка из FixedUpdate).
    /// </summary>
    public bool TryAttack()
    {
        if (cooldownTimer > 0) return false;

        var gm = GridManager.Instance;
        if (gm == null) return false;

        List<Unit> targets = new List<Unit>();

        foreach (Vector2Int cell in GetAttackCells())
        {
            if (!gm.InBounds(cell)) continue;

            Unit occupant = gm.GetOccupant(cell);
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
    /// Получить урон: списывает hp, запускает вспышку matHit, при hp<=0 — Die().
    /// </summary>
    public void TakeDamage(int dmg, Unit attacker)
    {
        hp -= dmg;
        Debug.Log($"[Combat] {name} получил {dmg} урона от {attacker.name} (HP: {hp}/{maxHp})");

        if (flashCoroutine != null) StopCoroutine(flashCoroutine);
        flashCoroutine = StartCoroutine(Flash());

        if (hp <= 0)
        {
            Die();
        }
    }

    /// <summary>
    /// Визуальная вспышка получения урона: временно подменяем материал рендерера
    /// на matHit, через 0.1 сек возвращаем исходный.
    /// </summary>
    private System.Collections.IEnumerator Flash()
    {
        var renderer = GetComponent<Renderer>();
        if (renderer == null || matHit == null) yield break;

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
    /// Смерть юнита: логируем, освобождаем клетку в GridManager,
    /// удаляем себя из состава отряда и Destroy(gameObject).
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
}