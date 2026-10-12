using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ИИ вражеского юнита (MonoBehaviour-компонент). Добавляется АВТОМАТИЧЕСКИ
/// в Unit.Init(), если unit.team == Team.Enemy.
///
/// Логика:
/// 1. Поиск ближайшего юнита team == Player в радиусе detectionRange (клетки,
///    Manhattan-расстояние от GridPos) — через GridManager.GetAllUnits().
/// 2. Если цель найдена и расстояние до неё > attackRange — строим путь
///    Pathfinding.AStar к клетке цели и идём по нему плавно КОРУТИНОЙ
///    MoveAlongPath (по образцу Unit.MoveAlongPath): Vector3.MoveTowards
///    со скоростью unit.moveSpeed, дедзона 0.01f, финальный snap в центр
///    целевой клетки.
/// 3. Если цель в зоне поражения (расстояние <= attackRange) — останавливаемся
///    и смотрим на цель (Facing + ApplyFacing); атаку выполняет CombatResolver,
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
/// Расстояния считаем в КЛЕТКАХ (Manhattan), чтобы не зависеть от cellSize.
/// </summary>
public class EnemyAI : MonoBehaviour
{
    [Header("Detection")]
    /// <summary>Радиус обнаружения игрока, в клетках (Manhattan).</summary>
    public float detectionRange = 10f;

    [Header("Engagement")]
    /// <summary>
    /// Дистанция вступления в бой (Manhattan, целое число клеток). Значение по
    /// факту сравнивается как int, дробная часть игнорируется. Default 1 =
    /// атаковать только с соседней клетки.
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
            // Цель в зоне поражения (п.3): останавливаемся, разворачиваемся лицом
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
    /// Построить путь A* от текущей клетки юнита к клетке цели и запустить
    /// корутину движения. Проходимости клеток ведёт Pathfinding (занятые —
    /// проходимы как промежуточные, goal проходима всегда). Пути нет — стоим на
    /// месте; попробуем снова на следующем пересчёте.
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

        List<Vector2Int> newPath = Pathfinding.AStar(unit.GridPos, target.GridPos);

        if (newPath == null)
        {
            // Логируем не чаще 1 раза в 5 секунд (по Time.time).
            if (Time.time - lastPathFailLogTime >= PATH_FAIL_LOG_INTERVAL)
            {
                lastPathFailLogTime = Time.time;
                Debug.LogWarning($"[EnemyAI] {unit.name}: путь до цели {target.name} " +
                                 $"({target.GridPos}) недоступен — стоим на месте.");
            }

            StopMovement();
            return;
        }

        path = newPath;

        // Единая точка запуска движения: стоп старой корутины -> старт новой.
        StartMovement(path[path.Count - 1]);
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

        // Согласование семантики IsMoving с Unit: ИИ движет юнитом — юнит «в пути».
        unit.IsMoving = true;

        moveCoroutine = StartCoroutine(MoveAlongPath(finalCell));
    }

    /// <summary>
    /// Остановить движение: остановить корутину, сбросить путь, flag IsMoving.
    /// Вызывается при вступлении в бой, потере цели и перед новым репатting'ом.
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
    }

    /// <summary>
    /// Корутина движения ПО КЛЕТКАМ пути — полный аналог Unit.MoveAlongPath:
    /// для каждой клетки пути while (Distance > 0.01f) MoveTowards к центру
    /// клетки со скоростью unit.moveSpeed, затем следующая. Единая дедзона
    /// 0.01f (как у игрока), финальный snap в GridToWorld(finalCell).
    /// Изменяется ТОЛЬКО transform.position — логическую позицию (GridPos /
    /// occupancy) ведёт Unit.FixedUpdate, там же работает «логическая коллизия»
    /// с занятыми клетками (откат к lastGridPos). GridPos здесь НЕ обновляем.
    /// </summary>
    private System.Collections.IEnumerator MoveAlongPath(Vector2Int finalCell)
    {
        for (int i = 0; i < path.Count; i++)
        {
            Vector3 waypointPos = GridManager.Instance.GridToWorld(path[i]);

            while (Vector3.Distance(transform.position, waypointPos) > 0.01f)
            {
                transform.position = Vector3.MoveTowards(
                    transform.position,
                    waypointPos,
                    unit.moveSpeed * Time.deltaTime
                );

                yield return null;
            }
        }

        // Финальный snap в центр целевой клетки (как у игрока; GridPos зафиксирует
        // FixedUpdate на следующем шаге).
        transform.position = GridManager.Instance.GridToWorld(finalCell);

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
    /// Расстояние между клетками в «клеточных» единицах (Manhattan).
    /// Возвращает int; сравнение с float-радиусами безопасно (приведение).
    /// </summary>
    private static int DistanceInCells(Vector2Int a, Vector2Int b)
    {
        return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
    }
}
