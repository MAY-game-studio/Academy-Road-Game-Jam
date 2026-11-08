using System;
using UnityEngine;

namespace Sunbearer
{
    public sealed class PlayerController : MonoBehaviour
    {
        [Header("Appearance")]
        [SerializeField] private Transform visual;
        [SerializeField] private Sprite fullBallSprite;
        [SerializeField] private Sprite sunBallSprite;
        [SerializeField] private Material sunCoronaMaterial;
        [Tooltip("Degrees from local right to the direction the sprite faces.")]
        [SerializeField] private float spriteForwardAngle;

        [Header("Movement")]
        [SerializeField, Min(0.1f)] private float freeMoveSpeed = 2.6f;
        [SerializeField, Min(1f)] private float freeTurnRate = 900f;
        [SerializeField, Min(0.1f)] private float smallPushSpeed = 2f;
        [SerializeField, Min(0.1f)] private float fullPushSpeed = 1f;
        [SerializeField, Min(1f)] private float smallTurnRate = 235f;
        [SerializeField, Min(1f)] private float fullTurnRate = 95f;
        [SerializeField, Min(0.05f)] private float bodyRadius = 0.29f;

        [Header("Stats and impact")]
        [SerializeField, Min(1f)] private float maxHealth = 120f;
        [SerializeField, Min(0f)] private float baseDamage = 20f;
        [SerializeField, Min(0f)] private float baseKnockback = 2.2f;
        [SerializeField, Min(0f)] private float pushDamageScale = 0.6f;
        [SerializeField, Min(0f)] private float shotDamageScale = 0.35f;

        [Header("Damage response")]
        [SerializeField, Min(0f)] private float hitInvulnerability = 0.75f;
        [SerializeField, Min(0f)] private float hitRepelRadius = 1.8f;
        [SerializeField, Min(0f)] private float hitRepelSpeed = 3.6f;
        [SerializeField, Min(0f)] private float hitSlideSpeed = 2.1f;

        [Header("Ball growth")]
        [SerializeField, Min(0f)] private float chargePerUnit = 0.014f;
        [SerializeField, Range(0f, 1f)] private float bumpChargeLoss = 0.30f;
        [SerializeField, Min(0.05f)] private float smallBallRadius = 0.19f;
        [SerializeField, Min(0.05f)] private float fullBallRadius = 0.55f;
        [SerializeField, Min(0.01f)] private float smallBallMass = 1f;
        [Tooltip("Automatically kept high enough for impact energy to grow as the ball slows.")]
        [SerializeField, Min(0.01f)] private float fullBallMass = 8f;

        [Header("Stardust attraction")]
        [SerializeField, Min(0.05f)] private float smallAttractionRadius = 0.95f;
        [SerializeField, Min(0.05f)] private float fullAttractionRadius = 2.1f;
        [SerializeField, Min(0.1f)] private float stardustPullSpeed = 2.6f;

        [Header("Launch")]
        [SerializeField, Min(0.1f)] private float smallShotSpeed = 7.5f;
        [SerializeField, Min(0.1f)] private float fullShotSpeed = 4.5f;
        [SerializeField, Min(0.1f)] private float smallShotRange = 3.5f;
        [SerializeField, Min(0.1f)] private float fullShotRange = 7f;
        [SerializeField, Min(0.05f)] private float partialShotBurstSpeed = 1.25f;
        [SerializeField, Range(0f, 0.35f)] private float launchInertia = 0.18f;
        [SerializeField, Min(0f)] private float recoilSpeed = 3.6f;
        [SerializeField, Min(0.1f)] private float recoilDeceleration = 14f;

        [Header("Push collisions")]
        [SerializeField, Min(0f)] private float breakRecoilSpeed = 5f;
        [SerializeField, Min(0f)] private float gapLaunchRecoilSpeed = 2.6f;
        [SerializeField, Min(0f)] private float pushHitRecovery = 0.16f;
        [SerializeField, Min(0f)] private float lightPushHitRecoilSpeed = 1.6f;
        [SerializeField, Range(0f, 1f)] private float heavyPushHitSpeedScale = 0.85f;

        private enum PushImpact
        {
            None,
            Shatter,
            Launch
        }

        private readonly Collider2D[] contacts = new Collider2D[12];
        private ContactFilter2D targetFilter;
        private SolarTrails trails;
        private StardustSpawner stardust;
        private OasisMonsterSpawner monsters;
        private Collider2D coastline;
        private IslandCrossing[] islandCrossings;
        private SpriteRenderer playerRenderer;
        private Sprite normalSprite;
        private Color normalColor;
        private SunBallVisual heldBall;
        private SunProjectile lastShot;
        private Vector2 heading = Vector2.right;
        private Vector2 moveInput;
        private Vector2 recoil;
        private Vector2 hitSlide;
        private Vector2 driveVelocity;
        private Vector2 safeGround;
        private KeyCode pushButton;
        private bool fireRequested;
        private bool fireStartsPush;
        private bool pushStartPending;
        private int absorbedStardust;
        private bool pushInterrupted;
        private bool impactRecoiling;
        private float blackHoleEndsAt;
        private float solarWingsEndsAt;
        private float invulnerableUntil;
        private float pushHitRecoveryUntil;
        private float pushHitSpeedLimit;

        internal const int StardustReserveCapacity = 3;
        private const float PowerUpDuration = 3f;

        internal event Action Died;

        public float Charge { get; private set; }
        public bool HasBall { get; private set; } = true;
        public bool IsPushing { get; private set; }
        public float CurrentSpeed { get; private set; }
        public float MaxHealth => maxHealth;
        public float Health { get; private set; }
        public float BallMass => GetMass(Charge);
        public float LastLaunchSpeed { get; private set; }
        public int LastReflections { get; private set; }
        internal int StoredStardust { get; private set; }
        internal float BodyRadius => bodyRadius;
        internal float InvulnerabilityRemaining => Mathf.Max(0f, invulnerableUntil - Time.time);
        internal float BlackHoleRemaining => Mathf.Max(0f, blackHoleEndsAt - Time.time);
        internal float SolarWingsRemaining => Mathf.Max(0f, solarWingsEndsAt - Time.time);
        private bool IsDead => Health <= 0f;
        internal bool CanStoreStardust => !IsDead && StoredStardust < StardustReserveCapacity;
        internal bool CanFeedBallWithStardust => !IsDead && IsPushing && Charge < 1f;
        internal float StardustAttractionRadius => Mathf.Lerp(smallAttractionRadius, fullAttractionRadius, Charge);
        internal float StardustPullSpeed => stardustPullSpeed;

        internal void Initialize(SolarTrails solarTrails, StardustSpawner stardustSpawner,
            OasisMonsterSpawner monsterSpawner, Collider2D seaCollider, IslandCrossing[] crossings)
        {
            trails = solarTrails;
            stardust = stardustSpawner;
            monsters = monsterSpawner;
            coastline = seaCollider;
            islandCrossings = crossings;
            safeGround = transform.position;
            Health = maxHealth;
            if (visual && visual.TryGetComponent(out playerRenderer))
            {
                normalSprite = playerRenderer.sprite;
                normalColor = playerRenderer.color;
            }
            targetFilter = new ContactFilter2D { useTriggers = false };
            targetFilter.SetLayerMask(ArenaPhysics.TargetMask);
            heldBall = SunBallVisual.Create("Held sun", transform, transform.position, sunBallSprite, sunCoronaMaterial, 4);
            heldBall.SetSize(Radius, Charge);
            heldBall.gameObject.SetActive(false);
        }

        private void Update()
        {
            int x = (Input.GetKey(KeyCode.D) ? 1 : 0) - (Input.GetKey(KeyCode.A) ? 1 : 0);
            int y = (Input.GetKey(KeyCode.W) ? 1 : 0) - (Input.GetKey(KeyCode.S) ? 1 : 0);
            moveInput = new Vector2(x, y).normalized;
            UpdatePushInput();
        }

        private void UpdatePushInput()
        {
            if (pushButton == KeyCode.None)
            {
                if (Input.GetKeyDown(KeyCode.Space))
                    pushButton = KeyCode.Space;
                else if (Input.GetKeyDown(KeyCode.Mouse0))
                    pushButton = KeyCode.Mouse0;

                pushStartPending = pushButton != KeyCode.None;
            }

            if (pushButton == KeyCode.None)
                return;

            bool released = Input.GetKeyUp(pushButton);
            if (!released && Input.GetKey(pushButton))
                return;

            if (released && !pushInterrupted && !fireRequested)
            {
                fireRequested = true;
                fireStartsPush = pushStartPending;
            }

            pushButton = KeyCode.None;
            pushStartPending = false;
            pushInterrupted = false;
        }

        private void FixedUpdate()
        {
            if (!trails)
                return;

            Vector2 start = transform.position;
            if (OverGap(start) && !trails.Covers(start))
            {
                start = safeGround;
                transform.position = start;
                recoil = Vector2.zero;
                hitSlide = Vector2.zero;
                driveVelocity = Vector2.zero;
            }

            impactRecoiling &= recoil.sqrMagnitude > 0f;
            if (fireRequested)
            {
                fireRequested = false;
                // A quick press and release can start the push and launch in one physics step.
                if (!impactRecoiling)
                {
                    if (fireStartsPush)
                        LoadStoredStardust();
                    if (TryLaunch(start, driveVelocity))
                        recoil -= heading * recoilSpeed;
                }
                fireStartsPush = false;
            }

            IsPushing = pushButton != KeyCode.None && !pushInterrupted && !impactRecoiling;
            if (IsPushing && pushStartPending)
            {
                LoadStoredStardust();
                pushStartPending = false;
            }

            float turnRate = IsPushing ? Mathf.Lerp(smallTurnRate, fullTurnRate, Charge) : freeTurnRate;
            TurnToward(moveInput, turnRate);

            float moveSpeed = IsPushing ? Mathf.Lerp(smallPushSpeed, fullPushSpeed, Charge) : freeMoveSpeed;
            if (trails.Covers(start))
                moveSpeed *= trails.SpeedMultiplier;
            if (SolarWingsRemaining > 0f)
                moveSpeed *= 1.25f;
            if (Time.fixedTime < pushHitRecoveryUntil)
                moveSpeed = Mathf.Min(moveSpeed, pushHitSpeedLimit);

            bool slidingFromHit = hitSlide.sqrMagnitude > 0f;
            bool driving = IsPushing || moveInput.sqrMagnitude > 0f;
            Vector2 walkDirection = driving && !impactRecoiling && !slidingFromHit && moveSpeed > 0f
                ? heading : Vector2.zero;
            Vector2 walkVelocity = walkDirection * moveSpeed;
            Vector2 impactVelocity = slidingFromHit ? hitSlide : recoil;
            float deltaTime = Time.fixedDeltaTime;
            bool pushingBall = IsPushing && HasBall;
            Vector2 position = Move(start, (walkVelocity + impactVelocity) * deltaTime, pushingBall,
                out PushImpact impact, out Collider2D ballObstacle);
            Vector2 motion = position - start;
            transform.position = position;
            if (slidingFromHit)
                hitSlide = Vector2.MoveTowards(hitSlide, Vector2.zero, recoilDeceleration * deltaTime);
            else
                recoil = Vector2.MoveTowards(recoil, Vector2.zero, recoilDeceleration * deltaTime);
            CurrentSpeed = motion.magnitude / deltaTime;

            if (!OverGap(position))
                safeGround = position;

            float previousCharge = Charge;
            float forwardDistance = Vector2.Dot(motion, walkDirection);
            driveVelocity = forwardDistance > 0.00001f ? motion / deltaTime : Vector2.zero;
            if (forwardDistance > 0.00001f)
            {
                HasBall = true;
                Charge = Mathf.Clamp01(Charge + forwardDistance * chargePerUnit);
            }

            if (IsPushing && HasBall)
            {
                if (impact == PushImpact.None && (!pushingBall || Charge > previousCharge))
                    impact = GetPushImpact(position, out ballObstacle);

                if (impact != PushImpact.None)
                {
                    if (Charge >= 1f && ballObstacle)
                        RitualTower.ReportFullBallHit(ballObstacle);
                    ResolvePushImpact(impact, position, walkVelocity);
                }
                else if (forwardDistance > 0.00001f)
                    HitWithHeldBall(position, forwardDistance / deltaTime);
            }

            UpdateAppearance(position);
        }

        private void LateUpdate()
        {
            if (!playerRenderer)
                return;

            if (fullBallSprite)
            {
                Sprite sprite = HasBall && Charge >= 1f ? fullBallSprite : normalSprite;
                if (playerRenderer.sprite != sprite)
                    playerRenderer.sprite = sprite;
            }

            Color color = normalColor;
            float remaining = InvulnerabilityRemaining;
            if (remaining > 0f && Mathf.Repeat((hitInvulnerability - remaining) * 6f, 1f) < 0.5f)
                color.a *= 0.28f;
            if (playerRenderer.color != color)
                playerRenderer.color = color;
        }

        internal bool TryGetRollingBall(out Vector2 center, out float radius)
        {
            if (heldBall && heldBall.gameObject.activeInHierarchy)
            {
                center = heldBall.transform.position;
                radius = heldBall.transform.localScale.x * 0.5f;
                return true;
            }

            center = default;
            radius = 0f;
            return false;
        }

        internal bool TryStoreStardust()
        {
            if (!CanStoreStardust)
                return false;

            StoredStardust++;
            return true;
        }

        internal bool TryFeedBallWithStardust(float amount)
        {
            if (!CanFeedBallWithStardust || !TryAddBallCharge(amount))
                return false;

            absorbedStardust++;
            return true;
        }

        private void LoadStoredStardust()
        {
            if (StoredStardust == 0)
                return;

            TryAddBallCharge(StoredStardust * stardust.ChargePerStardust);
            absorbedStardust += StoredStardust;
            StoredStardust = 0;
        }

        internal bool TryAddBallCharge(float amount)
        {
            if (Charge >= 1f || amount <= 0f)
                return false;

            HasBall = true;
            Charge = Mathf.Clamp01(Charge + amount);
            return true;
        }

        internal bool TryRestoreHealth(float amount)
        {
            if (IsDead || Health >= maxHealth || amount <= 0f)
                return false;

            Health = Mathf.Min(maxHealth, Health + amount);
            return true;
        }

        internal void TakeDamage(float amount, Vector2? sourcePosition = null)
        {
            if (IsDead || amount <= 0f || Time.time < invulnerableUntil)
                return;

            Health = Mathf.Max(0f, Health - amount);
            if (IsDead)
            {
                Died?.Invoke();
                return;
            }

            invulnerableUntil = Time.time + hitInvulnerability;
            Vector2 position = transform.position;
            Vector2 awayFromSource = sourcePosition.HasValue ? position - sourcePosition.Value : -heading;
            if (awayFromSource.sqrMagnitude <= 0.000001f)
                awayFromSource = -heading;

            hitSlide = awayFromSource.normalized * hitSlideSpeed;
            driveVelocity = Vector2.zero;
            if (monsters)
                monsters.RepelAround(position, hitRepelRadius, hitRepelSpeed);
        }

        internal void ActivateBlackHole()
        {
            blackHoleEndsAt = Time.time + PowerUpDuration;
        }

        internal void ActivateSolarWings()
        {
            solarWingsEndsAt = Time.time + PowerUpDuration;
        }

        internal void FillBall()
        {
            HasBall = true;
            Charge = 1f;
        }

        internal void ReportReflection(SunProjectile shot, int count)
        {
            if (shot == lastShot)
                LastReflections = count;
        }

        private void TurnToward(Vector2 direction, float turnRate)
        {
            if (direction.sqrMagnitude <= 0.0001f)
                return;

            float limit = turnRate * Time.fixedDeltaTime;
            float turn = Mathf.Clamp(Vector2.SignedAngle(heading, direction), -limit, limit);
            heading = Quaternion.Euler(0f, 0f, turn) * heading;
        }

        private Vector2 Move(Vector2 position, Vector2 motion, bool pushingBall,
            out PushImpact impact, out Collider2D ballObstacle)
        {
            ballObstacle = null;
            impact = pushingBall ? GetPushImpact(position, out ballObstacle) : PushImpact.None;
            if (impact != PushImpact.None)
                return position;

            int steps = Mathf.Max(1, Mathf.CeilToInt(motion.magnitude / 0.10f));
            Vector2 step = motion / steps;
            for (int i = 0; i < steps; i++)
            {
                if (pushingBall)
                {
                    Vector2 candidate = position + ClipBodyMotion(position, step);
                    impact = GetPushImpact(candidate, out ballObstacle);
                    if (impact != PushImpact.None)
                        break;

                    position = candidate;
                }
                else if (!TryStep(ref position, step) && step.x != 0f && step.y != 0f)
                {
                    TryStep(ref position, new Vector2(step.x, 0f));
                    TryStep(ref position, new Vector2(0f, step.y));
                }
            }

            return position;
        }

        private bool TryStep(ref Vector2 position, Vector2 step)
        {
            Vector2 motion = ClipBodyMotion(position, step);
            Vector2 candidate = position + motion;
            if (motion.sqrMagnitude <= 0f || GroundBlock(candidate, bodyRadius))
                return false;

            position = candidate;
            return true;
        }

        private Vector2 ClipBodyMotion(Vector2 position, Vector2 motion)
        {
            return monsters ? monsters.ClipAgainstMonsters(position, bodyRadius, motion) : motion;
        }

        private Collider2D GroundBlock(Vector2 position, float radius)
        {
            Collider2D solid = Physics2D.OverlapCircle(position, radius, ArenaPhysics.SolidMask);
            if (solid)
                return solid;

            Collider2D gap = Physics2D.OverlapCircle(position, radius, ArenaPhysics.GapMask);
            return gap && !trails.Covers(position) ? gap : null;
        }

        private PushImpact GetPushImpact(Vector2 position, out Collider2D ballObstacle)
        {
            Vector2 ballCenter = BallCenter(position, heading);
            ballObstacle = GroundBlock(ballCenter, Radius);
            PushImpact ballImpact = ImpactAt(ballCenter, ballObstacle);
            if (ballImpact == PushImpact.Shatter)
                return ballImpact;

            PushImpact bodyImpact = ImpactAt(position, GroundBlock(position, bodyRadius));
            return bodyImpact == PushImpact.None ? ballImpact : bodyImpact;
        }

        private PushImpact ImpactAt(Vector2 center, Collider2D obstacle)
        {
            if (!obstacle)
                return PushImpact.None;
            if (obstacle.gameObject.layer != ArenaPhysics.GapLayer)
                return PushImpact.Shatter;
            if (obstacle != coastline)
                return PushImpact.Launch;

            Vector2 contact = coastline.ClosestPoint(center);
            foreach (IslandCrossing crossing in islandCrossings)
            {
                if (crossing.Contains(contact))
                    return PushImpact.Launch;
            }

            return PushImpact.Shatter;
        }

        private void ResolvePushImpact(PushImpact impact, Vector2 position, Vector2 moveVelocity)
        {
            pushInterrupted = true;
            impactRecoiling = true;
            hitSlide = Vector2.zero;
            IsPushing = false;

            if (impact == PushImpact.Launch && TryLaunch(position, moveVelocity))
            {
                recoil = -heading * gapLaunchRecoilSpeed;
                return;
            }

            SunProjectile.ShowBurst(trails.transform, BallCenter(position, heading), Radius);
            recoil = -heading * breakRecoilSpeed;
            ClearBall();
        }

        private bool OverGap(Vector2 position)
        {
            return Physics2D.OverlapCircle(position, bodyRadius, ArenaPhysics.GapMask);
        }

        private float Radius => Mathf.Lerp(smallBallRadius, fullBallRadius, Charge);

        private Vector2 BallCenter(Vector2 position, Vector2 direction)
        {
            return position + direction * (bodyRadius + Radius + 0.06f);
        }

        private Collider2D BallWall(Vector2 position, Vector2 direction)
        {
            Vector2 center = BallCenter(position, direction);
            Collider2D solid = Physics2D.OverlapCircle(center, Radius, ArenaPhysics.SolidMask);
            if (solid)
                return solid;

            // A thin gate can lie between the player and an otherwise clear launch center.
            return Physics2D.Linecast(position, center, 1 << ArenaPhysics.BoundaryLayer).collider;
        }

        private void HitWithHeldBall(Vector2 position, float speed)
        {
            int count = Physics2D.OverlapCircle(BallCenter(position, heading), Radius, targetFilter, contacts);
            for (int i = 0; i < count; i++)
            {
                if (!contacts[i].TryGetComponent(out ISolarBallTarget target))
                    continue;

                float mass = BallMass;
                float energy = RelativeEnergy(mass, speed, smallPushSpeed);
                if (target.Hit(baseDamage * pushDamageScale * energy, heading * (baseKnockback * energy), true))
                {
                    ApplyPushHitResponse(mass, speed);
                    ChipBall();
                    break;
                }
            }
        }

        private void ApplyPushHitResponse(float mass, float speed)
        {
            float massFraction = Mathf.InverseLerp(smallBallMass, GetMass(1f), mass);
            pushHitRecoveryUntil = Time.fixedTime + pushHitRecovery;
            pushHitSpeedLimit = massFraction >= 2f / 3f ? speed * heavyPushHitSpeedScale : 0f;
            recoil = massFraction < 1f / 3f ? -heading * lightPushHitRecoilSpeed : Vector2.zero;
            driveVelocity = Vector2.ClampMagnitude(driveVelocity, pushHitSpeedLimit);
        }

        private void ChipBall()
        {
            Charge = Mathf.Max(0f, Charge - bumpChargeLoss);
        }

        private bool TryLaunch(Vector2 position, Vector2 moveVelocity)
        {
            if (!HasBall || BallWall(position, heading))
                return false;

            float mass = GetMass(Charge);
            float muzzleSpeed = Mathf.Lerp(smallShotSpeed, fullShotSpeed, Charge);
            Vector2 impulse = heading * (mass * muzzleSpeed);
            Vector2 velocity = moveVelocity * launchInertia + impulse / mass;
            float energy = RelativeEnergy(mass, velocity.magnitude, smallShotSpeed);
            float range = Mathf.Lerp(smallShotRange, fullShotRange, Charge);
            SunBallVisual flight = heldBall.CreateFlight(trails.transform, BallCenter(position, heading), Radius, Charge);
            lastShot = SunProjectile.Launch(this, trails, stardust, flight, velocity, Radius,
                baseDamage * shotDamageScale * energy, baseKnockback * energy, range,
                full: Charge >= 1f, burstSpeed: partialShotBurstSpeed, stardustToScatter: absorbedStardust / 2);
            LastLaunchSpeed = velocity.magnitude;
            LastReflections = 0;
            ClearBall();
            return true;
        }

        private void ClearBall()
        {
            HasBall = false;
            Charge = 0f;
            absorbedStardust = 0;
        }

        private float GetMass(float charge)
        {
            // Offset the steeper of the two speed reductions so both impact energies rise with charge.
            float fullSpeedFraction = Mathf.Min(fullPushSpeed / smallPushSpeed, fullShotSpeed / smallShotSpeed);
            float speedFraction = Mathf.Lerp(1f, fullSpeedFraction, charge);
            float fullEnergyMass = fullBallMass * fullSpeedFraction * fullSpeedFraction;
            float energyMass = Mathf.Lerp(smallBallMass, Mathf.Max(smallBallMass * 1.05f, fullEnergyMass), charge);
            return energyMass / (speedFraction * speedFraction);
        }

        private float RelativeEnergy(float mass, float speed, float referenceSpeed)
        {
            float speedRatio = speed / referenceSpeed;
            return mass / smallBallMass * speedRatio * speedRatio;
        }

        private void UpdateAppearance(Vector2 position)
        {
            float angle = Mathf.Atan2(heading.y, heading.x) * Mathf.Rad2Deg;
            visual.localRotation = Quaternion.Euler(0f, 0f, angle - spriteForwardAngle);
            bool showBall = IsPushing && HasBall && !BallWall(position, heading);
            heldBall.transform.position = BallCenter(position, heading);
            heldBall.SetSize(Radius, Charge);
            if (heldBall.gameObject.activeSelf != showBall)
                heldBall.gameObject.SetActive(showBall);
        }

        private void OnValidate()
        {
            fullPushSpeed = Mathf.Min(fullPushSpeed, smallPushSpeed);
            fullTurnRate = Mathf.Min(fullTurnRate, smallTurnRate);
            fullShotSpeed = Mathf.Min(fullShotSpeed, smallShotSpeed);
            fullBallRadius = Mathf.Max(fullBallRadius, smallBallRadius);
            fullAttractionRadius = Mathf.Max(fullAttractionRadius, smallAttractionRadius);
            fullShotRange = Mathf.Max(fullShotRange, smallShotRange);
        }
    }
}
