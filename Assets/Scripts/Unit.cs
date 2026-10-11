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

    /// <summary>
    /// Инициализировать юнита: задать позицию и зарезервировать за ним клетку
    /// в GridManager. Если клетка уже занята/недоступна — логируем ошибку и
    /// НЕ запускаем логику спавна.
    /// </summary>
    public void Init(Vector2Int cell, Squad squad)
    {
        GridPos = cell;
        Squad = squad;

        // Шаг 8: инициализация боя — hp стартует с максимума. ВАЖНО: maxHp/hp
        // задаются ДО TryOccupy, чтобы юнит мог сразу погибнуть в бою, не успев
        // «пожить» с нулевым здоровьем.
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
    /// к направлению +X. В плоскости (X, Z) «синусной» координатой угла от +Z
    /// является X, а «косинной» — проекция на Z (у нас ось Y сетки = мир +Z).
    /// Поэтому аргументы идут как (x, y): Facing=(0,1) даёт 0° (смотреть +Z),
    /// Facing=(1,0) даёт +90° (yaw к +X). Если бы использовали Atan2(y, x),
    /// юниты «смотрели» бы на 90° в сторону от реального направления.
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
    /// ПО ПУТИ A* (обход камней).
    ///
    /// ВАЖНО (исправление бага телепортации): к моменту вызова GridPos УЖЕ равен
    /// targetCell — Squad.CommandFormation обновляет логику позицию и резервирует
    /// новую клетку ДО запуска движения. Поэтому стартовую клетку нельзя брать
    /// из GridPos (A* искал бы путь «от цели к цели», возвращал пустой путь, и
    /// финальная доводка превращалась в телепорт). Стартовая клетка передаётся
    /// явно параметром fromCell — это фактическая позиция трансформа до приказа.
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

        // Ищем путь через препятствия: start = ФАКТИЧЕСКАЯ стартовая клетка
        // (fromCell), а НЕ GridPos — он уже обновлён вызывающей стороной.
        List<Vector2Int> path = Pathfinding.AStar(fromCell, targetCell);

        if (path == null)
        {
            Debug.LogWarning($"[Unit] StartMove: путь недоступен ({fromCell} -> {targetCell}) — " +
                             $"юнит {name} остаётся на месте.", this);
            IsMoving = false;
            return;
        }

        // Останавливаем предыдущее движение, если оно шло (иначе две корутины
        // будут бороться за transform.position).
        if (moveCoroutine != null)
        {
            StopCoroutine(moveCoroutine);
        }

        moveCoroutine = StartCoroutine(MoveAlongPath(path, targetCell));
    }

    /// <summary>
    /// Корутина движения ПО КЛЕТКАМ пути: для каждой клетки пути последовательно
    /// MoveTowards к GridToWorld(cell) со скоростью moveSpeed клеток в секунду,
    /// затем следующая клетка. Изменяется ТОЛЬКО transform.position — rotation
    /// (взгляд по Facing, «нос») не трогается.
    /// targetCell передаётся для финальной точной доводки в центр цели.
    /// По окончании IsMoving = false.
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

            // Точная установка в центр пройденной клетки (без остатка в 0.01).
            transform.position = waypointPos;
        }

        // Финальная доводка в центр целевой клетки.
        transform.position = GridManager.Instance.GridToWorld(targetCell);

        IsMoving = false;
        moveCoroutine = null;
    }

    private void OnDestroy()
    {
        // При уничтожении освобождаем клетку, чтобы она снова стала доступной.
        // Оставлено как есть (по ТЗ): если Die() уже вызвал FreeUnit, словарь
        // occupancy клетки больше не содержит — Free сработает вхолостую
        // (идемпотентность occupant == unit защищает от двойного освобождения).
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
    /// right — перпендикуляр Facing «вправо» по часовой стрелке:
    /// right = (Facing.y, -Facing.x).
    /// Клетки: GridPos + Facing*1 + right*r, r = -1..1.
    ///
    /// РАСЧЁТ ДЛЯ Facing=(0,1) (мир +Z), юнит в (x,y):
    ///   right = (1, 0);
    ///   r=-1 -> (x+1-1, y+1+0) = (x,   y+1)
    ///   r= 0 -> (x+1, y+1)     = (x+1, y+1)
    ///   r=+1 -> (x+1+1, y+1)   = (x+2, y+1)
    /// То есть три клетки фронта: (x, y+1), (x+1, y+1), (x+2, y+1) — ряд на
    /// один шаг «вперёд» по Facing, центрированный чуть правее; крайние лежат
    /// по диагоналям от юнита, средняя — строго перед ним.
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
    /// Попытка атаки: наносит attackDamage всем вражеским юнитам в зоне атаки.
    /// Условия: не движется, кулдаун истёк, в зоне есть хотя бы один юнит
    /// ЧУЖОЙ команды. Возвращает true, если удар состоялся.
    /// </summary>
    public bool TryAttack()
    {
        if (IsMoving) return false;
        if (cooldownTimer > 0) return false;

        var gm = GridManager.Instance;
        if (gm == null) return false;

        List<Unit> targets = new List<Unit>();

        foreach (Vector2Int cell in GetAttackCells())
        {
            if (!gm.InBounds(cell)) continue;

            Unit occupant = gm.GetOccupant(cell);
            if (occupant != null && occupant.team != this.team)
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
    /// attacker пока используется только для логов (в будущем — килл-статы).
    /// </summary>
    public void TakeDamage(int dmg, Unit attacker)
    {
        hp -= dmg;
        Debug.Log($"[Combat] {name} получил {dmg} урона от {attacker.name} (HP: {hp}/{maxHp})");

        // Перезапускаем вспышку, если старая ещё идёт — иначе эффекты наслаиваются
        // и материал может остаться «подсвеченным».
        if (flashCoroutine != null) StopCoroutine(flashCoroutine);
        flashCoroutine = StartCoroutine(Flash());

        if (hp <= 0)
        {
            Die();
        }
    }

    /// <summary>
    /// Визуальная вспышка получения урона: временно подменяем материал рендерера
    /// на matHit, через 0.1 сек возвращаем исходный. Если объект уничтожен
    /// (юнит умер) во время ожидания — проверка this != null защитит от
    /// обращения к удалённому рендереру.
    /// </summary>
    private System.Collections.IEnumerator Flash()
    {
        var renderer = GetComponent<Renderer>();
        if (renderer == null || matHit == null) yield break;

        Material original = renderer.material;
        renderer.material = matHit;

        yield return new WaitForSeconds(0.1f);

        // this != null — проверка «unity-null»: объект мог быть Destroy'нут
        // после смерти юнита, тогда возвращать материал уже некому.
        if (this != null && renderer != null)
        {
            renderer.material = original;
        }

        flashCoroutine = null;
    }

    /// <summary>
    /// Смерть юнита: логируем, освобождаем клетку в GridManager (FreeUnit —
    /// идемпотентен) и удаляем GameObject. OnDestroy освободит повторно,
    /// но Free проверяет occupant == unit, так что дублей нет.
    /// </summary>
    public void Die()
    {
        Debug.Log($"[Combat] {name} погиб");

        if (GridManager.Instance != null)
        {
            GridManager.Instance.FreeUnit(this);
        }

        Destroy(gameObject);
    }
}
