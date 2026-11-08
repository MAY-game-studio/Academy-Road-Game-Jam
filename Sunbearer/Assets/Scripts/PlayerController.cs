using UnityEngine;

namespace Sunbearer
{
    public sealed class PlayerController : MonoBehaviour
    {
        [SerializeField] private Transform visual;

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

        private readonly Collider2D[] contacts = new Collider2D[12];
        private ContactFilter2D targetFilter;
        private SolarTrails trails;
        private StardustSpawner stardust;
        private Camera viewCamera;
        private SpriteRenderer heldBall;
        private SunProjectile lastShot;
        private Vector2 heading = Vector2.right;
        private Vector2 moveInput;
        private Vector2 aimPoint;
        private Vector2 recoil;
        private Vector2 driveVelocity;
        private Vector2 safeGround;
        private bool wantsPush;
        private bool fireRequested;
        private int absorbedStardust;
        private float nextWallBump;

        public float Charge { get; private set; }
        public bool HasBall { get; private set; } = true;
        public bool IsPushing { get; private set; }
        public float CurrentSpeed { get; private set; }
        public float MaxHealth => maxHealth;
        public float BallMass => GetMass(Charge);
        public float LastLaunchSpeed { get; private set; }
        public int LastReflections { get; private set; }
        internal bool CanCollectStardust => IsPushing && Charge < 1f;
        internal float StardustAttractionRadius => Mathf.Lerp(smallAttractionRadius, fullAttractionRadius, Charge);
        internal float StardustPullSpeed => stardustPullSpeed;

        internal void Initialize(SolarTrails solarTrails, Camera camera, StardustSpawner stardustSpawner)
        {
            trails = solarTrails;
            stardust = stardustSpawner;
            viewCamera = camera;
            safeGround = transform.position;
            targetFilter = new ContactFilter2D { useTriggers = false };
            targetFilter.SetLayerMask(ArenaPhysics.TargetMask);
            heldBall = ArenaSprites.Create("Held sun", transform, transform.position, Vector2.one,
                ArenaSprites.Disc, Color.white, 4);
            heldBall.enabled = false;
        }

        private void Update()
        {
            int x = (Input.GetKey(KeyCode.D) ? 1 : 0) - (Input.GetKey(KeyCode.A) ? 1 : 0);
            int y = (Input.GetKey(KeyCode.W) ? 1 : 0) - (Input.GetKey(KeyCode.S) ? 1 : 0);
            moveInput = new Vector2(x, y).normalized;
            wantsPush = Input.GetKey(KeyCode.Space);
            if (wantsPush && viewCamera)
            {
                Vector3 cursor = Input.mousePosition;
                cursor.z = viewCamera.WorldToScreenPoint(transform.position).z;
                aimPoint = viewCamera.ScreenToWorldPoint(cursor);
            }

            fireRequested |= Input.GetKeyUp(KeyCode.Space);
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
                driveVelocity = Vector2.zero;
            }

            if (fireRequested)
            {
                fireRequested = false;
                // Launch using the last push before applying free movement for this step.
                Fire(start, driveVelocity);
            }

            IsPushing = wantsPush;
            Vector2 desiredHeading = IsPushing ? aimPoint - start : moveInput;
            float turnRate = IsPushing ? Mathf.Lerp(smallTurnRate, fullTurnRate, Charge) : freeTurnRate;
            TurnToward(start, desiredHeading, turnRate);

            float moveSpeed = IsPushing ? Mathf.Lerp(smallPushSpeed, fullPushSpeed, Charge) : freeMoveSpeed;
            Vector2 walkDirection = IsPushing || moveInput.sqrMagnitude > 0f ? heading : Vector2.zero;
            if (trails.Covers(start))
                moveSpeed *= trails.SpeedMultiplier;

            Vector2 walkVelocity = walkDirection * moveSpeed;
            float deltaTime = Time.fixedDeltaTime;
            bool protectBall = IsPushing && HasBall && !BallWall(start, heading);
            Vector2 position = Move(start, (walkVelocity + recoil) * deltaTime, protectBall, out bool ballBumped);
            Vector2 motion = position - start;
            transform.position = position;
            recoil = Vector2.MoveTowards(recoil, Vector2.zero, recoilDeceleration * deltaTime);
            CurrentSpeed = motion.magnitude / deltaTime;

            if (!OverGap(position))
                safeGround = position;

            float forwardDistance = Vector2.Dot(motion, walkDirection);
            driveVelocity = forwardDistance > 0.00001f ? motion / deltaTime : Vector2.zero;
            if (forwardDistance > 0.00001f)
            {
                HasBall = true;
                Charge = Mathf.Clamp01(Charge + forwardDistance * chargePerUnit);
            }

            if (IsPushing && HasBall)
            {
                if (ballBumped && Time.fixedTime >= nextWallBump)
                {
                    ChipBall();
                    nextWallBump = Time.fixedTime + 0.30f;
                }

                if (forwardDistance > 0.00001f && !BallWall(position, heading))
                    HitWithHeldBall(position, forwardDistance / deltaTime);
            }

            UpdateAppearance(position);
        }

        internal bool TryGetRollingBall(out Vector2 center, out float radius)
        {
            if (heldBall && heldBall.enabled)
            {
                center = heldBall.transform.position;
                radius = heldBall.transform.localScale.x * 0.5f;
                return true;
            }

            center = default;
            radius = 0f;
            return false;
        }

        internal bool TryCollect(float amount)
        {
            if (!CanCollectStardust || amount <= 0f)
                return false;

            HasBall = true;
            Charge = Mathf.Clamp01(Charge + amount);
            absorbedStardust++;
            return true;
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

        private void TurnToward(Vector2 position, Vector2 direction, float turnRate)
        {
            if (direction.sqrMagnitude <= 0.0001f)
                return;

            float limit = turnRate * Time.fixedDeltaTime;
            float turn = Mathf.Clamp(Vector2.SignedAngle(heading, direction), -limit, limit);
            Vector2 next = Quaternion.Euler(0f, 0f, turn) * heading;
            if (!IsPushing || !HasBall || !BallWall(position, next) || BallWall(position, heading))
                heading = next;
        }

        private Vector2 Move(Vector2 position, Vector2 motion, bool protectBall, out bool ballBumped)
        {
            ballBumped = false;
            int steps = Mathf.Max(1, Mathf.CeilToInt(motion.magnitude / 0.10f));
            Vector2 step = motion / steps;
            for (int i = 0; i < steps; i++)
            {
                if (TryStep(ref position, step, protectBall, ref ballBumped))
                    continue;

                if (step.x != 0f && step.y != 0f)
                {
                    TryStep(ref position, new Vector2(step.x, 0f), protectBall, ref ballBumped);
                    TryStep(ref position, new Vector2(0f, step.y), protectBall, ref ballBumped);
                }
            }

            return position;
        }

        private bool TryStep(ref Vector2 position, Vector2 step, bool protectBall, ref bool ballBumped)
        {
            Vector2 candidate = position + step;
            if (Physics2D.OverlapCircle(candidate, bodyRadius, ArenaPhysics.SolidMask) ||
                (OverGap(candidate) && !trails.Covers(candidate)))
                return false;

            if (protectBall && BallWall(candidate, heading))
            {
                ballBumped = true;
                return false;
            }

            position = candidate;
            return true;
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
            return Physics2D.OverlapCircle(BallCenter(position, direction), Radius, ArenaPhysics.SolidMask);
        }

        private void HitWithHeldBall(Vector2 position, float speed)
        {
            int count = Physics2D.OverlapCircle(BallCenter(position, heading), Radius, targetFilter, contacts);
            for (int i = 0; i < count; i++)
            {
                if (!contacts[i].TryGetComponent(out PracticeTarget target))
                    continue;

                float energy = RelativeEnergy(GetMass(Charge), speed, smallPushSpeed);
                if (target.Hit(baseDamage * pushDamageScale * energy, heading * (baseKnockback * energy), true))
                {
                    ChipBall();
                    break;
                }
            }
        }

        private void ChipBall()
        {
            if (Charge < 1f)
                Charge = Mathf.Max(0f, Charge - bumpChargeLoss);
        }

        private void Fire(Vector2 position, Vector2 moveVelocity)
        {
            if (!HasBall || BallWall(position, heading))
                return;

            float mass = GetMass(Charge);
            float muzzleSpeed = Mathf.Lerp(smallShotSpeed, fullShotSpeed, Charge);
            Vector2 impulse = heading * (mass * muzzleSpeed);
            Vector2 velocity = moveVelocity * launchInertia + impulse / mass;
            float energy = RelativeEnergy(mass, velocity.magnitude, smallShotSpeed);
            float range = Mathf.Lerp(smallShotRange, fullShotRange, Charge);
            lastShot = SunProjectile.Launch(this, trails, stardust, BallCenter(position, heading), velocity, Radius,
                baseDamage * shotDamageScale * energy, baseKnockback * energy, range,
                full: Charge >= 1f, burstSpeed: partialShotBurstSpeed, stardustToScatter: absorbedStardust / 2);
            LastLaunchSpeed = velocity.magnitude;
            LastReflections = 0;
            recoil -= heading * recoilSpeed;
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
            visual.localRotation = Quaternion.Euler(0f, 0f, angle);
            heldBall.enabled = IsPushing && HasBall && !BallWall(position, heading);
            heldBall.transform.position = BallCenter(position, heading);
            heldBall.transform.localScale = Vector3.one * (Radius * 2f);
            heldBall.color = Color.Lerp(new Color(1f, 0.53f, 0.19f), new Color(1f, 0.84f, 0.27f), Charge);
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
