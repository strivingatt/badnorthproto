using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ИИ вражеского юнита (MonoBehaviour-компонент). Добавляется АВТОМАТИЧЕСКИ
/// в Unit.Init(), если unit.team == Team.Enemy.
///
/// Логика:
/// 1. Поиск ближайшего юнита team == Player в радиусе detectionRange (клетки,
///    метрика Чебышёва max(|dx|,|dy|) от GridPos — Fix-1, баг Б; раньше был
///    Manhattan, из-за чего диагональный сосед считался «далёким» и враг
///    строил путь В клетку цели) — через GridManager.GetAllUnits().
/// 2. Если цель найдена и НЕ смежна с нами (Чебышёв > attackRange) — строим
///    путь Pathfinding.AStar к ближайшей СВОБОДНОЙ клетке, смежной с целью
///    (Чебышёв = 1 от target.GridPos), и идём по нему плавно КОРУТИНОЙ
///    MoveAlongPath (по образцу Unit.MoveAlongPath): Vector3.MoveTowards
///    со скоростью unit.moveSpeed, дедзона 0.01f, финальный snap в центр
///    целевой клетки. В саму клетку цели путь НЕ строится — это исключает
///    ситуацию «два юнита в одной клетке».
///    РЕЕСТР КЛЕЙМОВ (фикс «слияния врагов»): несколько врагов, преследующих
///    одну цель, раньше выбирали одну и ту же свободную смежную клетку и
///    въезжали в неё вдвоём. Теперь целевая клетка ТЕКУЩЕГО движения
///    «заклеймляется» в статическом множестве claimedCells, общем для всех
///    экземпляров EnemyAI: кандидат, уже выбранный другим врагом, отбрасывается.
///    Реестр НЕ является частью occupancy и НЕ влияет на бой (зоны атаки,
///    GetAllUnits, кулдаун его не видят). Клейм освобождается при достижении
///    цели, при новом пересчёте пути и в OnDestroy (покрывает смерть юнита).
/// 3. Если цель в зоне поражения (Чебышёв <= attackRange, т.е. смежность по
///    8 направлениям) — останавливаемся (MoveTowards НЕ вызываем) и смотрим
///    на цель (Facing + ApplyFacing); атаку выполняет CombatResolver,
///    вызывая TryAttack() с общего тика — сам ИИ НЕ дерёт.
/// 4. Если цель умерла (unity-null) или ушла за пределы detectionRange — сброс
///    и поиск новой цели.
/// 5. Путь пересчитывается НЕ каждый кадр, а с интервалом repathInterval (0.5с)
///    — для производительности. Между пересчётами юнит просто идёт по текущему
///    пути (корoutine сама доходит до конца пути).
///
/// ПОЧЕМУ КОРУТИНА, А НЕ ДВИЖЕНИЕ В UPDATE (исправление конфликта записи):
/// Update() ИИ трогает ТОЛЬКО логику (цель, таймеры, разворот, старт/стоп
/// корутины) и НИКОГДА не пишет transform.position. Единственные писатели
/// позиции — корутина ИИ (пока идёт) и Unit.FixedUpdate (откат при «логической
/// коллизии» с занятой клеткой), как у юнита игрока. Если бы ИИ двигал трансформ
/// в Update (Time.deltaTime), а FixedUpdate одновременно делал откат
/// (FixedUpdate-шагами), две системы писали бы в transform.position с разными
/// частотами — юнит дёргался бы на границах клеток. Корутина живёт в том же
/// слое, что и движение игрока, — поведение идентично.
///
/// Координатные оси: Vector2Int (x, y) сетки -> мир (x, z); ось Y сетки = мир +Z.
/// Расстояния считаем в КЛЕТКАХ (Чебышёв), чтобы не зависеть от cellSize.
/// </summary>
public class EnemyAI : MonoBehaviour
{
    [Header("Detection")]
    /// <summary>Радиус обнаружения игрока, в клетках (Чебышёв).</summary>
    public float detectionRange = 10f;

    [Header("Engagement")]
    /// <summary>
    /// Дистанция вступления в бой (Чебышёв, целое число клеток). Значение по
    /// факту сравнивается как int, дробная часть игнорируется. Default 1 =
    /// атаковать только с соседней клетки (смежность по 8 направлениям).
    /// (Раньше стояло 1.5f, что для целочисленного Manhattan эквивалентно 1 —
    /// семантическая ложь; приведено к честному значению.)
    /// </summary>
    public float attackRange = 1f;

    [Header("Performance")]
    /// <summary>Интервал пересчёта пути и цели, секунд.</summary>
    public float repathInterval = 0.5f;

    /// <summary>Юнит-хозяин ИИ (тот же GameObject).</summary>
    private Unit unit;

    /// <summary>Текущая цель (юнит команды Player) или null.</summary>
    private Unit target;

    /// <summary>Активный путь к цели (null — не идём).</summary>
    private List<Vector2Int> path;

    /// <summary>Таймер до следующего пересчёта цели/пути.</summary>
    private float repathTimer;

    /// <summary>Активная корутина движения ИИ (останавливается при новом приказе).</summary>
    private Coroutine moveCoroutine;

    /// <summary>Время последней записи warning о недоступном пути (анти-спам).</summary>
    private float lastPathFailLogTime = 0f;

    /// <summary>Минимальный интервал между логами «путь недоступен», секунд.</summary>
    private const float PATH_FAIL_LOG_INTERVAL = 5f;

    /// <summary>
    /// Общий реестр «заклеймленных» целевых клеток движения (фикс слияния врагов).
    /// Клейм = клетка, которую враг выбрал целью ТЕКУЩЕГО движения. Реестр НЕ
    /// является частью occupancy и НЕ влияет на бой: зоны атаки, GetAllUnits и
    /// кулдаун его не видят — это только координация выбора цели движения между
    /// экземплярами EnemyAI.
    /// </summary>
    private static readonly HashSet<Vector2Int> claimedCells = new HashSet<Vector2Int>();

    /// <summary>Собственный текущий клейм этого врага (null — не заклеймлено).</summary>
    private Vector2Int? myClaimedCell = null;

    private void Awake()
    {
        unit = GetComponent<Unit>();

        if (unit == null)
        {
            Debug.LogError("[EnemyAI] На объекте нет компонента Unit — ИИ отключён.", this);
            enabled = false;
            return;
        }

        // Защита от конфликта с Unit.StartMove: если компонент добавлен в момент,
        // когда чья-то корутина движения уже шла, сбрасываем флаг. ИИ дальше сам
        // выставляет IsMoving=true при старте своей корутины и false при остановке
        // (семантика поля согласована с Unit).
        unit.IsMoving = false;
    }

    /// <summary>
    /// Уничтожение компонента (в т.ч. смерть юнита — Destroy в Unit.Die):
    /// освобождаем собственный клейм, чтобы клетка не осталась «виртуально
    /// занятой» для других врагов навсегда.
    /// </summary>
    private void OnDestroy()
    {
        ReleaseClaim();
    }

    /// <summary>
    /// Освободить собственный предыдущий клейм из общего реестра (если есть).
    /// Вызывается перед новым выбором цели движения, при достижении цели и в OnDestroy.
    /// </summary>
    private void ReleaseClaim()
    {
        if (myClaimedCell.HasValue)
        {
            claimedCells.Remove(myClaimedCell.Value);
            myClaimedCell = null;
        }
    }

    /// <summary>
    /// Заклеймить выбранную цель движения за собой.
    /// </summary>
    private void SetClaim(Vector2Int cell)
    {
        ReleaseClaim();
        claimedCells.Add(cell);
        myClaimedCell = cell;
    }

    private void Update()
    {
        if (GridManager.Instance == null || unit == null)
        {
            return;
        }

        // Таймер пересчёта: цели и пути обновляются НЕ каждый кадр (п.5 ТЗ).
        repathTimer += Time.deltaTime;

        bool shouldRepath = repathTimer >= repathInterval;

        if (shouldRepath)
        {
            repathTimer = 0f;
            AcquireTarget();
        }

        if (target == null)
        {
            // Цели нет — простаиваем (цель могла умереть или уйти за радиус:
            // unity-null отсеивается в AcquireTarget, п.4).
            StopMovement();
            return;
        }

        int dist = DistanceInCells(unit.GridPos, target.GridPos);

        if (dist <= attackRange)
        {
            // Цель смежна с нами (Чебышёв <= 1 — зона атаки, п.3 ТЗ Fix-1):
            // останавливаемся (MoveTowards НЕ вызываем), разворачиваемся лицом
            // к цели. Атаковать будет CombatResolver своим тиком TryAttack().
            StopMovement();
            FaceTarget();
            return;
        }

        // Цель слишком далеко (п.2): нужно подойти.
        // Стартуем новый путь, если пересчёт был или корутина уже дошла до конца
        // (moveCoroutine == null — значит прежний путь исчерпан/остановлен).
        if (shouldRepath || moveCoroutine == null)
        {
            RepathToTarget();
        }

        // Сам transform.position здесь НЕ трогаем — движение делает корутина.
    }

    /// <summary>
    /// Поиск ближайшего живого юнита Player в радиусе detectionRange (п.1, п.4).
    /// Мёртвый/ушедший за радиус объект отсеивается здесь же: при неудаче target
    /// обнуляется и на следующем пересчёте будет найдена новая цель.
    /// </summary>
    private void AcquireTarget()
    {
        var gm = GridManager.Instance;
        Unit best = null;
        int bestDist = int.MaxValue;

        List<Unit> all = gm.GetAllUnits();

        for (int i = 0; i < all.Count; i++)
        {
            Unit u = all[i];

            // Unity-null: юнит мог быть Destroy'нут (мертв) — пропускаем (п.4).
            if (u == null || u.gameObject == null)
            {
                continue;
            }

            if (u.team != Team.Player)
            {
                continue;
            }

            // Сам себя не выбираем (на всякий случай: team хозяина Enemy, но защита дешёвая).
            if (u == unit)
            {
                continue;
            }

            int d = DistanceInCells(unit.GridPos, u.GridPos);

            if (d <= detectionRange && d < bestDist)
            {
                bestDist = d;
                best = u;
            }
        }

        target = best;
    }

    /// <summary>
    /// Построить путь A* от текущей клетки юнита к ближайшей СВОБОДНОЙ клетке,
    /// смежной с целью (Чебышёв = 1 от target.GridPos), и запустить корутину
    /// движения. В саму клетку цели путь НЕ строится (Fix-1, баг Б): иначе
    /// враг залезал в занятую клетку — два юнита в одной клетке, нулевой вектор
    /// направления, никто не бьёт. Проходимость клеток ведёт Pathfinding.
    ///
    /// РЕЕСТР КЛЕЙМОВ: кандидат additionally отфильтровывается по признаку
    /// «не заклеймлена другим врагом как цель его текущего движения» — иначе
    /// несколько врагов выбирали одну и ту же свободную клетку и въезжали в неё
    /// вдвоём (слияние). Если подходящего кандидата нет — враг остаётся на месте
    /// в этот тик пересчёта (без fallback-пути в клетку цели и без входа в
    /// занятую клетку); попробует снова на следующем пересчёте.
    /// Перед новым выбором освобождается предыдущий собственный клейм.
    ///
    /// Анти-спам (п.4 правок): warning о недоступном пути печатается не чаще
    /// одного раза в PATH_FAIL_LOG_INTERVAL (5 сек) на одного врага — иначе при
    /// 20+ осаждённых целях консоль заливается каждые 0.5 сек.
    /// </summary>
    private void RepathToTarget()
    {
        if (target == null || target.gameObject == null)
        {
            StopMovement();
            return;
        }

        // П.2 ТЗ: перед новым выбором освобождаем свой предыдущий клейм.
        // NB: только клейм, НЕ StopMovement() — иначе остановка активной
        // корутины на середине пути оставила бы трансформ между клетками
        // (snap не выполнен). Клейм переставляется в SetClaim при старте.
        ReleaseClaim();

        Vector2Int? goal = FindAdjacentFreeCell(target.GridPos);

        if (goal == null)
        {
            // П.3 ТЗ: ни одна смежная с целью клетка не подошла (вне границ /
            // камень / занята / заклеймена другим врагом) — стоим на месте.
            // Вход в клетку цели и в занятую клетку ЗАПРЕЩЁН.
            if (Time.time - lastPathFailLogTime >= PATH_FAIL_LOG_INTERVAL)
            {
                lastPathFailLogTime = Time.time;
                Debug.LogWarning($"[EnemyAI] {unit.name}: у цели {target.name} нет свободной " +
                                 "незаклеймленной смежной клетки — стоим на месте, " +
                                 "пересчёт в следующем тике.");
            }

            StopMovement();
            return;
        }

        List<Vector2Int> newPath = Pathfinding.AStar(unit.GridPos, goal.Value);

        if (newPath == null)
        {
            // Логируем не чаще 1 раза в 5 секунд (по Time.time).
            if (Time.time - lastPathFailLogTime >= PATH_FAIL_LOG_INTERVAL)
            {
                lastPathFailLogTime = Time.time;
                Debug.LogWarning($"[EnemyAI] {unit.name}: путь до цели {target.name} " +
                                 $"({goal}) недоступен — стоим на месте.");
            }

            StopMovement();
            return;
        }

        path = newPath;

        // Единая точка запуска движения: стоп старой корутины -> старт новой.
        StartMovement(goal.Value);
    }

    /// <summary>
    /// Выбрать цель пути: из 8 клеток, смежных с целью (Чебышёв = 1 от
    /// targetCell), берём ближайшую к нам по Чебышёву среди подходящих:
    /// в границах, не заблокирована (stone), GetOccupant == null,
    /// НЕ заклеймлена другим врагом как цель его текущего движения.
    /// Подходящих нет — возвращаем null (враг стоит на месте; в клетку цели
    /// и в занятую клетку входить запрещено). Клетка самого юнита в
    /// рассмотрение не входит (мы там уже стоим).
    /// </summary>
    private Vector2Int? FindAdjacentFreeCell(Vector2Int targetCell)
    {
        var gm = GridManager.Instance;

        Vector2Int? best = null;
        int bestDist = int.MaxValue;

        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue; // сама клетка цели — не цель пути

                Vector2Int cell = targetCell + new Vector2Int(dx, dy);

                if (cell == unit.GridPos) continue; // мы там уже стоим

                if (!gm.InBounds(cell)) continue;   // вне сетки

                if (gm.IsBlocked(cell)) continue;   // камень/препятствие

                if (gm.GetOccupant(cell) != null) continue; // занят юнитом

                if (claimedCells.Contains(cell)) continue;  // заклеймён другим врагом

                int dist = DistanceInCells(unit.GridPos, cell);

                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = cell;
                }
            }
        }

        return best;
    }

    /// <summary>
    /// Запустить корутину движения по активному пути (this.path). finalCell —
    /// последняя клетка пути, в неё делается финальный snap.
    /// Перед стартом останавливаем предыдущую корутину — иначе две будут
    /// бороться за transform.position.
    /// </summary>
    private void StartMovement(Vector2Int finalCell)
    {
        if (moveCoroutine != null)
        {
            StopCoroutine(moveCoroutine);
        }

        // Клейм выбранной цели движения за собой: другие враги при следующем
        // выборе эту клетку не рассмотрят (фикс слияния врагов).
        SetClaim(finalCell);

        // Согласование семантики IsMoving с Unit: ИИ движет юнитом — юнит «в пути».
        unit.IsMoving = true;

        moveCoroutine = StartCoroutine(MoveAlongPath(finalCell));
    }

    /// <summary>
    /// Остановить движение: остановить корутину, сбросить путь, флаг IsMoving.
    /// Вызывается при вступлении в бой, потере цели и перед новым репатting'ом.
    ///
    /// ВАЖНО: движение остановлено -> цель движения больше не нужна.
    /// Освобождаем клейм, чтобы другие враги могли занять эту клетку,
    /// когда мы умрем или отойдем.
    /// </summary>
    private void StopMovement()
    {
        path = null;

        if (moveCoroutine != null)
        {
            StopCoroutine(moveCoroutine);
            moveCoroutine = null;
        }

        if (unit != null)
        {
            unit.IsMoving = false;
        }

        ReleaseClaim();
    }

    /// <summary>
    /// Корутина движения ПО КЛЕТКАМ пути — полный аналог Unit.MoveAlongPath:
    /// для каждой клетки пути while (Distance > 0.01f) MoveTowards к центру
    /// клетки со скоростью unit.moveSpeed, затем следующая. Единая дедзона
    /// 0.01f (как у игрока). Изменяется ТОЛЬКО transform.position — логическую
    /// позицию (GridPos / occupancy) ведёт Unit.FixedUpdate, там же работает
    /// «логическая коллизия» с занятыми клетками.
    /// GridPos здесь НЕ обновляем.
    ///
    /// СТРАХОВКА (п.5 ТЗ): проверяем занятость ПОСЛЕДНЕЙ клетки пути ДО того,
    /// как в неё заехать; если она уже занята другим юнитом (кто-то успел встать
    /// туда, пока мы шли) — прерываем цикл и остаёмся на последней пройденной
    /// СВОБОДНОЙ клетке пути, а не в занятой. Иначе — финальный snap в центр
    /// безопасной клетки.
    /// При любом завершении корутины освобождаем собственный клейм (п.4 ТЗ:
    /// движение закончено — цель движения больше не актуальна).
    /// </summary>
    private System.Collections.IEnumerator MoveAlongPath(Vector2Int finalCell)
    {
        // Запоминаем текущую позицию как безопасную (на случай пустого пути)
        Vector2Int lastFreeCell = unit.GridPos;

        for (int i = 0; i < path.Count; i++)
        {
            Vector2Int targetCell = path[i];

            // Страховка: если это ПОСЛЕДНЯЯ клетка и она уже занята (кто-то въехал),
            // прерываем цикл и остаёмся на предыдущей.
            if (i == path.Count - 1 && GridManager.Instance.GetOccupant(targetCell) != null)
            {
                Debug.LogWarning($"[EnemyAI] {unit.name}: конечная клетка {targetCell} занята — остановка на {lastFreeCell}.");
                break;
            }

            Vector3 waypointPos = GridManager.Instance.GridToWorld(targetCell);

            while (Vector3.Distance(transform.position, waypointPos) > 0.01f)
            {
                transform.position = Vector3.MoveTowards(
                    transform.position,
                    waypointPos,
                    unit.moveSpeed * Time.deltaTime
                );

                yield return null;
            }

            // Обновляем последнюю безопасную клетку только после успешного прибытия
            lastFreeCell = targetCell;
        }

        // Финальный snap в центр безопасной клетки
        // (как у игрока; GridPos зафиксирует FixedUpdate на следующем шаге).
        transform.position = GridManager.Instance.GridToWorld(lastFreeCell);

        // П.4 ТЗ: движение завершено — наш клейм больше не нужен.
        ReleaseClaim();

        moveCoroutine = null;
        unit.IsMoving = false;
    }

    /// <summary>
    /// Развернуть юнита лицом к цели (чтобы зона атаки GetAttackCells смотрела
    /// в сторону противника). Facing приводится к одному из 8 секторов сетки:
    /// по компонентам разницы позиций берём знак; диагональ сохраняется.
    /// После установки Facing — unit.ApplyFacing() (тот же поворот, что у игрока).
    /// </summary>
    private void FaceTarget()
    {
        if (target == null || target.gameObject == null)
        {
            return;
        }

        Vector2Int diff = target.GridPos - unit.GridPos;

        Vector2Int facing = new Vector2Int(
            Mathf.Clamp(diff.x, -1, 1),
            Mathf.Clamp(diff.y, -1, 1)
        );

        // Совпадение клеток невозможно (dist > 0), но защита от нулевого вектора:
        if (facing == Vector2Int.zero)
        {
            facing = new Vector2Int(0, 1);
        }

        if (unit.Facing != facing)
        {
            unit.Facing = facing;
            unit.ApplyFacing();
        }
    }

    /// <summary>
    /// Расстояние между клетками в «клеточных» единицах метрикой Чебышёва
    /// max(|dx|, |dy|) (Fix-1, баг Б: вместо Manhattan — диагональный сосед
    /// теперь дистанция 1, т.е. «в зоне атаки», а не 2).
    /// Возвращает int; сравнение с float-радиусами безопасно (приведение).
    /// </summary>
    private static int DistanceInCells(Vector2Int a, Vector2Int b)
    {
        return Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y));
    }
}
