using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Юнит на сетке. Хранит логическую позицию, направление взгляда и принадлежность
/// к отряду. Движение — по клеткам пути A* (обход заблокированных клеток).
///
/// ВИЗУАЛ: модель может содержать несколько рендереров (MeshRenderer тела,
/// SkinnedMeshRenderer меча и т.п.). Перекраска (SetVisual) и вспышка урона
/// (Flash) работают с кэшем всех рендереров модели (rendererCache), нос
/// (объект "Nose") исключён.
///
/// АНИМАЦИЯ: Animator опционален (может отсутствовать у старых префабов-кубов).
/// Используется ТОЛЬКО для триггера атаки: SetTrigger("Attack").
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
    public int maxHp = 5;

    /// <summary>Текущее здоровье (только чтение извне).</summary>
    private int hp;
    public int Hp => hp;

    /// <summary>
    /// Лечение: восстанавливает amount HP, но не выше maxHp.
    /// Мёртвый юнит не лечится (Fix-4 защита от двойной смерти сохранена).
    /// </summary>
    public void Heal(int amount)
    {
        if (isDead) return;
        hp = Mathf.Min(maxHp, hp + amount);
    }

    /// <summary>Урон одной атаки (дефолт переопределяется классами-наследниками).</summary>
    public int attackDamage = 1;

    /// <summary>Длительность перезарядки атаки, секунд (дефолт — у Militia).</summary>
    public float attackCooldown = 0.8f;

    /// <summary>
    /// Дистанция, на которой ИИ должен остановиться, чтобы атаковать
    /// (клетки, Чебышёв). Базовый Unit не знает форм атаки — значение по
    /// умолчанию 1 (ближний бой); классы переопределяют (Копейщик — 2,
    /// Пращник — 3). Используется EnemyAI вместо жёсткого attackRange.
    /// </summary>
    public virtual int PreferredAttackRange => 1;

    /// <summary>
    /// Клетки зоны атаки для ПРОИЗВОЛЬНОЙ позиции origin и разворота facing —
    /// чистая геометрия, без обращения к текущему состоянию юнита. Нужна ИИ,
    /// чтобы заранее проверить/выбрать клетку боя. Базовый Unit форм атаки не
    /// знает: список пуст (не бьёт вовсе); переопределяют боевые классы.
    /// </summary>
    public virtual List<Vector2Int> GetAttackCells(Vector2Int origin, Vector2Int facing)
    {
        return new List<Vector2Int>(0);
    }

    /// <summary>
    /// Клетки зоны атаки при текущих GridPos/Facing — обёртка над
    /// GetAttackCells(origin, facing), используется ударом Militia.
    /// </summary>
    public List<Vector2Int> GetAttackCells()
    {
        return GetAttackCells(GridPos, Facing);
    }

    /// <summary>
    /// Достаёт ли юнит ударом клетку targetCell, находясь на клетке from:
    /// юнит сам развернётся в сторону цели (FacingToward), поэтому проверка НЕ
    /// зависит от текущего Facing. Базовая реализация — по геометрии зоны атаки,
    /// поэтому корректна для Militia/Warrior/Spearman. Классы с НЕзональной
    /// атакой (Slinger — выстрел по радиусу с линией видимости) переопределяют.
    ///
    /// Нужен ИИ, чтобы не стоять в «мёртвой зоне»: раньше вступление в бой
    /// проверялось только дистанцией (Чебышёв &lt;= PreferredAttackRange), и
    /// Копейщик, реальная зона которого — ровно дистанция 2, замирал, когда цель
    /// подходила вплотную: два копейщика просто смотрели друг на друга (P0).
    /// </summary>
    public virtual bool CanAttackCellFrom(Vector2Int from, Vector2Int targetCell)
    {
        if (from == targetCell) return false; // нулевой вектор направления

        Vector2Int facing = FacingToward(from, targetCell);

        return GetAttackCells(from, facing).Contains(targetCell);
    }

    /// <summary>
    /// Разворот из клетки from в сторону клетки to, квантованный до одного из
    /// 8 секторов сетки: компоненты разницы сжимаются до знака, диагональ
    /// сохраняется. Для совпадающих клеток возвращается (0,1) — «вперёд»
    /// (защита от нулевого направления, как было в EnemyAI.FaceTarget).
    /// </summary>
    public static Vector2Int FacingToward(Vector2Int from, Vector2Int to)
    {
        Vector2Int diff = to - from;

        Vector2Int facing = new Vector2Int(
            Mathf.Clamp(diff.x, -1, 1),
            Mathf.Clamp(diff.y, -1, 1)
        );

        return facing == Vector2Int.zero ? new Vector2Int(0, 1) : facing;
    }

    /// <summary>
    /// Анимация атаки: базовый Unit не знает форм удара — производные классы
    /// вызывают этот хук из своей TryAttack() после успешного выбора целей.
    /// </summary>
    protected void PlayAttackAnimation()
    {
        // Триггер (не Play/bool) — повторные атаки корректно перезапускают клип.
        // animator может быть null у старых префабов-кубов.
        if (animator != null)
        {
            animator.SetTrigger("Attack");
        }
    }

    /// <summary>Оставшееся время до следующей атаки, секунд. Тикает снаружи
    /// CombatResolver (поле остаётся public — контракт резолвера не меняется);
    /// классы-наследники проверяют его перед ударом и запускают после.</summary>
    public float cooldownTimer = 0f;

    /// <summary>
    /// Флаг смерти (Fix-1, баг В): Destroy(gameObject) отложен до конца кадра,
    /// поэтому при двух ударах в одном боевом тике Die() мог сработать дважды
    /// (двойной лог смерти, повторный Squad.Remove). Все боевые входы
    /// (TakeDamage / TryAttack / Die) проверяют этот флаг.
    /// </summary>
    protected bool isDead = false;

    /// <summary>Жив ли юнит (только чтение извне — для ИИ и боевых систем).</summary>
    public bool IsDead => isDead;

    /// <summary>
    /// Допуск при сравнении кулдауна с нулём: таймер уменьшается на tickInterval
    /// (float), поэтому после «восьми тиков по 0.1» в нём может остаться ~1e-8.
    /// Без допуска юнит изредка пропускал бы лишний тик.
    /// </summary>
    protected const float CooldownEpsilon = 0.0001f;

    /// <summary>Материал вспышки при получении урона (назначается спавнером).</summary>
    public Material matHit;

    /// <summary>Активная корутина вспышки «получил урон».</summary>
    private Coroutine flashCoroutine;

    /// <summary>Активная корутина движения (чтобы останавливать при повторном приказе).</summary>
    private Coroutine moveCoroutine;

    /// <summary>«Родной» материал юнита (белый у игроков, красный у врагов):
    /// запоминается при спавне, восстанавливается после снятия выделения.</summary>
    private Material defaultMaterial;

    /// <summary>
    /// Пары «рендерер → его оригинальный материал» для возврата после вспышки
    /// урона. Строится в Init() через BuildRendererCache() ПОСЛЕ создания носа;
    /// нос в кэш не входит. Инстанс материала создаётся один раз при построении
    /// кэша (Fix-4, баг А) и переиспользуется всеми присваиваниями.
    /// </summary>
    private readonly List<(Renderer renderer, Material original, Material instance)> rendererCache
        = new List<(Renderer, Material, Material)>();

    /// <summary>
    /// Animator модели (может быть null — у старых префабов-кубов аниматора нет).
    /// Используется только для триггера атаки: SetTrigger("Attack").
    /// </summary>
    private Animator animator;

    // --- Авто-поворот к ближайшему врагу ---

    /// <summary>Таймер пересчёта направления взгляда.</summary>
    private float facingUpdateTimer = 0f;

    /// <summary>Авто-поворот только в бою: боевой радиус, клетки (Чебышёв).</summary>
    private const int CombatFacingRange = 6;

    /// <summary>Интервал пересчёта направления, секунд (не каждый кадр).</summary>
    private const float FacingUpdateInterval = 0.2f;

    /// <summary>
    /// Таблица 8 секторов направлений (тот же порядок, что в Formation.cs):
    /// индекс 0 = Восток (+X), далее против часовой: NE, N(+Y), NW, W, SW, S, SE.
    /// </summary>
    private static readonly Vector2Int[] Sectors =
    {
        new Vector2Int(1, 0),
        new Vector2Int(1, 1),
        new Vector2Int(0, 1),
        new Vector2Int(-1, 1),
        new Vector2Int(-1, 0),
        new Vector2Int(-1, -1),
        new Vector2Int(0, -1),
        new Vector2Int(1, -1),
    };

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

        // Применяем текущее направление: локальный +Z юнита смотрит вдоль Facing.
        ApplyFacing();

        // Кэшируем все рендереры модели (тело + меч и т.п.), кроме носа.
        BuildRendererCache();

        // Animator может отсутствовать (старые префабы-кубы) — это допустимо,
        // анимация атаки просто не проиграется. Ищем и в дочерних объектах
        // (аниматор обычно живёт на модели), включая выключенные — иначе на
        // префабах с отключённой моделью триггер атаки молча терялся.
        animator = GetComponentInChildren<Animator>(true);

        // ИИ врагов: если это вражеский юнит — навешиваем EnemyAI кодом
        // (компонент появляется автоматически сразу после регистрации на сетке).
        if (team == Team.Enemy && GetComponent<EnemyAI>() == null)
        {
            gameObject.AddComponent<EnemyAI>();
        }
    }

    /// <summary>
    /// Собрать все рендереры модели (MeshRenderer, SkinnedMeshRenderer и т.п.),
    /// исключив объект "Nose". Для каждого рендерера создаётся ОДИН material-
    /// инстанс (Fix-4, баг А): присваивание renderer.material само по себе
    /// клонирует материал на каждое обращение, поэтому копия создается ровно
    /// один раз при построении кэша и закрепляется через sharedMaterial —
    /// дальнейшие перекраски пишут в этот же инстанс (renderer.sharedMaterial),
    /// новых копий НЕ появляется. В кэш сохраняются «оригинал» (родной
    /// sharedMaterial до клонирования) и инстанс. Инстанс автоматически
    /// высвобождается Unity вместе с рендерером при уничтожении GameObject
    /// (см. OnDestroy). includeInactive = true — на случай, если часть модели
    /// временно отключена.
    /// </summary>
    private void BuildRendererCache()
    {
        rendererCache.Clear();

        Renderer[] all = GetComponentsInChildren<Renderer>(true);

        foreach (Renderer r in all)
        {
            if (r == null) continue;

            // Исключаем нос (указатель направления) — он не перекрашивается.
            if (r.gameObject.name == "Nose") continue;

            Material original = r.sharedMaterial;

            if (original != null)
            {
                // Одноразовый клон: первое обращение к getter'у renderer.material
                // после присваивания возвращает ту же копию (без нового клона).
                r.material = original;
                Material instance = r.material;
                r.sharedMaterial = instance; // фиксируем инстанс для всех будущих записей
                rendererCache.Add((r, original, instance));
            }
            else
            {
                // Материала нет — клонировать нечего; SetVisual запишет напрямую.
                rendererCache.Add((r, null, null));
            }
        }

        if (rendererCache.Count == 0)
        {
            Debug.LogWarning($"[Unit] {name}: не найдено ни одного рендерера (кроме носа) — " +
                             $"перекраска и вспышка урона работать не будут.", this);
        }
    }

    private void Update()
    {
        // Авто-поворот: пересчёт направления не каждый кадр, а с интервалом 0.2с.
        facingUpdateTimer += Time.deltaTime;

        if (facingUpdateTimer >= FacingUpdateInterval)
        {
            facingUpdateTimer = 0f;
            UpdateFacing();
        }
    }

    /// <summary>Боевой радиус авто-поворота, клеток (Чебышёв, Fix-2 п.2).</summary>
    public const int AutoFaceCombatRange = CombatFacingRange;

    /// <summary>
    /// Пересчитать направление взгляда (Fix-2, баг Б: перебивание facing'а
    /// формации). Авто-поворот включается ТОЛЬКО в бою:
    /// - юнит НЕ движется (IsMoving == false);
    /// - есть живой враг в боевом радиусе (Чебышёв от GridPos <= CombatFacingRange).
    /// Если условие не выполнено — Facing НЕ меняется (держится от последнего
    /// приказа CommandFormation или предыдущего врага), поэтому направление
    /// строя живёт дольше 0.2 с в покое.
    /// Facing врагов ведёт ТОЛЬКО EnemyAI — для team != Player early-return.
    /// Метрика унифицирована: везде Чебышёв max(|dx|,|dy|).
    /// </summary>
    public void UpdateFacing()
    {
        // Facing врагов ведёт только EnemyAI (Fix-2, п.2): авто-поворот игрока
        // не должен вмешиваться в поведение вражеских юнитов.
        if (team != Team.Player)
        {
            return;
        }

        // Во время движения facing не трогаем — он задан приказом формации.
        if (IsMoving)
        {
            return;
        }

        Unit enemy = FindNearestEnemyInCombatRange();

        if (enemy == null)
        {
            return; // врага в боевом радиусе нет — сохраняем текущий Facing
        }

        Vector2Int dir = enemy.GridPos - GridPos;

        // Совпадение клеток невозможно, но защита от нулевого вектора нужна:
        // Atan2(0,0) вернёт 0 и юнит развернулся бы без причины.
        if (dir == Vector2Int.zero)
        {
            return;
        }

        // Квантование до 8 секторов. Таблица Sectors обходит по часовой от +X,
        // поэтому угол берём как Atan2(dir.y, dir.x) — от +X к +Y.
        float angle = Mathf.Atan2(dir.y, dir.x);
        int k = Mathf.RoundToInt(angle / (Mathf.PI * 0.25f));
        k = ((k % 8) + 8) % 8;

        Vector2Int newFacing = Sectors[k];

        if (newFacing != Facing)
        {
            Facing = newFacing;
            ApplyFacing();
        }
    }

    /// <summary>
    /// Найти ближайшего живого врага в БОЕВОМ радиусе (Чебышёв <= CombatFacingRange, клетки).
    /// Метрика — Чебышёв max(|dx|,|dy|), та же, что у EnemyAI (Fix-2, п.2:
    /// унификация меры). Возвращает Unit или null.
    /// </summary>
    private Unit FindNearestEnemyInCombatRange()
    {
        var gm = GridManager.Instance;

        if (gm == null)
        {
            return null;
        }

        Unit nearest = null;
        int bestDist = int.MaxValue;

        List<Unit> all = gm.GetAllUnits();

        for (int i = 0; i < all.Count; i++)
        {
            Unit u = all[i];

            if (u == null || u.gameObject == null || u.isDead || u.team == this.team)
            {
                continue;
            }

            int dx = Mathf.Abs(u.GridPos.x - GridPos.x);
            int dy = Mathf.Abs(u.GridPos.y - GridPos.y);
            int dist = Mathf.Max(dx, dy); // Чебышёв

            if (dist <= CombatFacingRange && dist < bestDist)
            {
                bestDist = dist;
                nearest = u;
            }
        }

        return nearest;
    }

    /// <summary>
    /// Синхронизация логической позиции с фактической (БЕЗ физики): каждый
    /// FixedUpdate читаем клетку под transform.position и, если юнит вошёл в
    /// новую клетку, обновляем GridPos и occupancy. Юниты не блокируют друг
    /// друга, поэтому в одной клетке может оказаться несколько юнитов —
    /// учёт хранит их списком (см. GridManager.occupancy).
    /// </summary>
    private void FixedUpdate()
    {
        var gm = GridManager.Instance;

        if (gm == null) return;

        Vector2Int currentCell = gm.WorldToGrid(transform.position);

        // Deadzone: не фиксируем смену клетки, пока юнит не подъехал
        // достаточно близко к её центру.
        Vector3 currentWorldPos = gm.GridToWorld(currentCell);
        float distToCenter = Vector3.Distance(transform.position, currentWorldPos);
        if (distToCenter > 0.4f) return;

        if (currentCell != GridPos && gm.InBounds(currentCell))
        {
            gm.Free(GridPos, this);
            GridPos = currentCell;
            gm.ForceOccupy(GridPos, this);
        }

        // Самовосстановление учёта: живой юнит обязан быть зарегистрирован
        // в своей клетке. Иначе он выпадает из GetAllUnits и становится
        // невидимым для боя и ИИ (например, если CommandFormation освободил
        // клетку, а путь к слоту не нашёлся).
        if (gm.InBounds(GridPos) && !gm.IsOccupiedBy(GridPos, this))
        {
            gm.ForceOccupy(GridPos, this);
        }
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
    /// Сменить визуальный цвет/текстуру у ВСЕХ рендереров кэша (тело + меч и т.п.).
    /// Обновляет «оригиналы» в кэше, чтобы вспышка урона возвращала актуальный
    /// цвет (например, после подсветки выделения).
    /// Реализация (Fix-4, баг А): НЕ присваивает renderer.material — каждое
    /// такое присваивание клонирует материал, и в бою со вспышками копия
    /// накопилась бы на каждый удар. Вместо этого пишем свойства переданного
    /// материала в УЖЕ СОЗДАННЫЙ инстанс рендерера (создаётся один раз в
    /// BuildRendererCache). Если инстанса нет (рендерер был без материала) —
    /// назначаем mat как sharedMaterial напрямую (без клонирования).
    /// </summary>
    public void SetVisual(Material mat)
    {
        if (mat == null)
        {
            Debug.LogWarning("[Unit] SetVisual: передан null-материал.", this);
            return;
        }

        for (int i = 0; i < rendererCache.Count; i++)
        {
            var (r, _, instance) = rendererCache[i];

            if (r == null) continue;

            if (instance != null)
            {
                // Переиспользуем существующий инстанс: копируем свойства,
                // новых material-объектов не появляется.
                ApplyMaterialProps(instance, mat);
            }
            else
            {
                // Инстанса нет (рендерер был без материала) — назначаем
                // sharedMaterial: ссылка на тот же объект, без клонирования.
                // ВАЖНО: instance оставляем null — mat является общим
                // ассетом-оригиналом, его уничтожать нельзя (иначе
                // сломает все юниты, использующие этот материал).
                r.sharedMaterial = mat;
                rendererCache[i] = (r, mat, null);
                continue;
            }

            // «Оригинал» обновляем на переданный материал: вспышка должна
            // возвращать актуальный цвет (например, после подсветки выделения).
            rendererCache[i] = (r, mat, rendererCache[i].instance);
        }
    }

    /// <summary>
    /// Скопировать ВСЕ свойства из src в dst (переиспользуемый инстанс
    /// материала). CopyPropertiesFromMaterial надёжно переносит цвета,
    /// текстуры и float-свойства независимо от шейдера — работает и в
    /// Built-in (_Color), и при переходе на URP (_BaseColor).
    /// </summary>
    private static void ApplyMaterialProps(Material dst, Material src)
    {
        dst.CopyPropertiesFromMaterial(src);
    }

    /// <summary>
    /// Назначить «родной» материал юнита и сразу применить его.
    /// Вызывается при спавне (SquadSpawner) — чтобы юнит помнил свой исходный
    /// цвет и мог восстановить его после снятия выделения.
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

    // --- ЛОГИКА АТАКИ ВЫНЕСЕНА В КЛАССЫ ---
    // Таблица Rot45, GetAttackCells() и реализация удара переехали в Militia.cs
    // (система классов войск). Базовый Unit не знает конкретных форм атаки;
    // дистанция боя задаётся через PreferredAttackRange, анимация — PlayAttackAnimation().

    /// <summary>
    /// Попытка атаки, вызывается CombatResolver каждый боевой тик (0.1 с).
    /// Базовый Unit НЕ реализует удар — возвращает false. Реальную атаку
    /// выполняют классы-наследники (Militia — ближний бой по зоне 3 клетки,
    /// Spearman — через клетку, Slinger — выстрел с линией видимости).
    /// </summary>
    public virtual bool TryAttack()
    {
        return false;
    }

    /// <summary>
    /// Получить урон: минус HP, вспышка ВСЕХ рендереров, смерть при HP <= 0.
    /// При цепочке быстрых ударов: перед остановкой старой корутины
    /// восстанавливаем оригиналы вручную — иначе matHit может «застрять».
    /// </summary>
    public void TakeDamage(int dmg, Unit attacker)
    {
        // Мёртвый юнит не получает урон (Fix-1, баг В): второй удар в том же
        // тике не должен повторно списывать HP и звать Die().
        if (isDead) return;

        hp -= dmg;
        Debug.Log($"[Combat] {name} получил {dmg} урона (HP: {hp}/{maxHp})");

        if (flashCoroutine != null)
        {
            StopCoroutine(flashCoroutine);
            flashCoroutine = null;

            RestoreAllOriginals();
        }

        flashCoroutine = StartCoroutine(Flash());

        if (hp <= 0)
        {
            // Убийца передаётся в Die(), чтобы засчитать убийство отряду-атакующему.
            Die(attacker);
        }
    }

    /// <summary>
    /// Визуальная вспышка «получил урон»: подмена цвета ВСЕХ рендереров кэша
    /// на цвет matHit на 0.1 сек, затем возврат каждого «оригинала».
    /// (Fix-4, баг А) Пишем свойства в переиспользуемые инстансы материалов —
    /// renderer.material больше не присваивается, новых копий не возникает.
    /// </summary>
    private IEnumerator Flash()
    {
        if (matHit == null || rendererCache.Count == 0)
            yield break;

        foreach (var pair in rendererCache)
        {
            if (pair.renderer != null && pair.instance != null)
            {
                ApplyMaterialProps(pair.instance, matHit);
            }
        }

        yield return new WaitForSecondsRealtime(0.1f);

        if (gameObject != null)
        {
            RestoreAllOriginals();
        }

        flashCoroutine = null;
    }

    /// <summary>
    /// Пройти по кэшу и вернуть каждому рендереру его оригинальный материал
    /// (копией свойств в переиспользуемый инстанс — без клонирования).
    /// </summary>
    private void RestoreAllOriginals()
    {
        foreach (var pair in rendererCache)
        {
            if (pair.renderer != null && pair.original != null && pair.instance != null)
            {
                ApplyMaterialProps(pair.instance, pair.original);
            }
        }
    }

    /// <summary>
    /// Смерть юнита: остановить вспышку, освободить клетку, учесть потерю/
    /// убийство в счётчиках отрядов, убрать себя из отряда, уничтожить объект.
    /// </summary>
    /// <param name="killer">Юнит, нанёсший смертельный удар (может быть null —
    /// тогда убийство не засчитывается никому, потеря отряда учитывается всегда).</param>
    public void Die(Unit killer = null)
    {
        // Защита от двойной смерти (Fix-1, баг В): Destroy(gameObject) отложен
        // до конца кадра, поэтому при двух ударах в одном тике Die() мог
        // вызваться дважды (двойной лог, повторный Remove из Squad).
        if (isDead) return;
        isDead = true;

        // Учёт боя: свой отряд всегда теряет бойца; убийство засчитывается
        // отряду убийцы только если он из ДРУГОЙ команды (дружественный урон
        // убийством не считается).
        if (Squad != null)
        {
            Squad.RegisterLoss();
        }

        if (killer != null && killer.team != team && killer.Squad != null)
        {
            killer.Squad.RegisterKill();
        }

        string killerName = (killer != null) ? killer.name : "нет";
        int ownLosses = (Squad != null) ? Squad.losses : 0;
        int killerKills = (killer != null && killer.Squad != null) ? killer.Squad.kills : 0;

        Debug.Log($"[Combat] {name} погиб (убийца: {killerName}, " +
                  $"потерь в своём отряде: {ownLosses}, убийств у отряда убийцы: {killerKills})");

        if (flashCoroutine != null)
        {
            StopCoroutine(flashCoroutine);
            flashCoroutine = null;
        }

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
    /// ПО ПУТИ A* (обход камней). Стартовая клетка передаётся ЯВНО.
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

        if (moveCoroutine != null)
        {
            StopCoroutine(moveCoroutine);
        }

        moveCoroutine = StartCoroutine(MoveAlongPath(path, targetCell));
    }

    /// <summary>
    /// Запустить перемещение по УЖЕ РАССЧИТАННОМУ пути A* (Fix-2, баг А):
    /// путь построен в Squad.CommandFormation (проход 0) ДО освобождения
    /// клеток, поэтому повторный A* здесь не нужен и юнит гарантированно
    /// не остаётся «призраком» (клетка освобождена только для валидного пути).
    /// Стартовая клетка передаётся ЯВНО (для логов/страховки).
    /// </summary>
    public void StartMoveAlongPath(List<Vector2Int> path, Vector2Int fromCell, Vector2Int targetCell)
    {
        if (GridManager.Instance == null)
        {
            Debug.LogError("[Unit] StartMoveAlongPath: GridManager.Instance == null — движение отменено.", this);
            return;
        }

        if (path == null || path.Count == 0)
        {
            Debug.LogWarning($"[Unit] StartMoveAlongPath: пустой путь ({fromCell} -> {targetCell}) — " +
                             $"юнит {name} остаётся на месте.", this);
            IsMoving = false;
            return;
        }

        if (moveCoroutine != null)
        {
            StopCoroutine(moveCoroutine);
        }

        moveCoroutine = StartCoroutine(MoveAlongPath(path, targetCell));
    }

    /// <summary>
    /// Корутина движения ПО КЛЕТКАМ пути: MoveTowards к центру каждой клетки
    /// пути со скоростью moveSpeed.
    /// </summary>
    private IEnumerator MoveAlongPath(List<Vector2Int> path, Vector2Int targetCell)
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

        transform.position = GridManager.Instance.GridToWorld(targetCell);

        IsMoving = false;
        moveCoroutine = null;
    }

    private void OnDestroy()
    {
        if (GridManager.Instance != null)
        {
            GridManager.Instance.Free(GridPos, this);
        }

        // Fix-4, баг А: уничтожаем созданные material-инстансы.
        // Unity позволяет и рекомендует удалять инстансы материалов,
        // созданные через renderer.material, именно внутри OnDestroy().
        foreach (var pair in rendererCache)
        {
            if (pair.instance != null)
            {
                Destroy(pair.instance);
            }
        }
        rendererCache.Clear();
    }
}