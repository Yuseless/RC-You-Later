using System.Collections.Generic;

using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(SphereCollider))]
[RequireComponent(typeof(Rigidbody))]
public class RemoteControlCarController : MonoBehaviour
{
    [SerializeField]
    private AnimationCurve motorPowerOverForwardSpeed = new(
        new Keyframe(0.0f, 4.0f),
        new Keyframe(10.0f, 10.0f),
        new Keyframe(20.0f, 0.0f)
        );

    [SerializeField]
    private float breakForce = 10.0f;

    /// <summary>
    /// https://docs.unity3d.com/2023.2/Documentation//ScriptReference/WheelFrictionCurve.html
    /// </summary>
    [SerializeField]
    private AnimationCurve sidewaysFriction = new(
        new Keyframe(0.0f, 0.0f),   // Starting point
        new Keyframe(0.2f, 1.0f),   // Extremum
        new Keyframe(0.5f, 0.75f)    // Asymptote
        );

    [SerializeField]
    private float stiffness = 10f;

    [SerializeField]
    private float frontWheelMaxAngle = 45.0f;

    [SerializeField]
    private float minForwardSpeedForRotation = 1.0f;

    [SerializeField]
    private float airRotationControl = 0.4f;

    [SerializeField]
    private Transform modelPivot = null;

    [SerializeField]
    private Transform frontLeftWheel = null;

    [SerializeField]
    private Transform frontRightWheel = null;

    [SerializeField]
    private GameObject[] groundParticles = new GameObject[0];

    private InputAction playerTurnAction = null;

    private InputAction playerAccelerateAction = null;

    private SphereCollider sphereCollider = null;

    private new Rigidbody rigidbody = null;



    /// <summary>
    /// Surface's normal below this threshold will be treated as ground (1: all, 0: half, -1: none).
    /// Default is -0.25f, meaning up until 67.5°.
    /// </summary>
    private float gravityGroundThreshold = -0.25f;

    private Vector3 averageGroundNormal = Vector3.up;

    private Vector3 previousLinearVelocity = Vector3.zero;

    private float maxRotRadPerSec = 180.0f * Mathf.Deg2Rad;

    private Dictionary<Collider, List<GroundContactPoint>> groundContactPoints = new Dictionary<Collider, List<GroundContactPoint>>(8);
    private float groundContactGracePeriod = 0.2f;
    private float lastGroundContactPointTime = 0.0f;

    public Vector3 GravityDirection
    {
        get
        {
            return Physics.gravity.normalized;
        }
    }

    public bool IsGrounded
    {
        get
        {
            return groundContactPoints.Count > 0 || (Time.fixedTime - lastGroundContactPointTime) < groundContactGracePeriod;
        }
    }

    public Vector3 GroundNormal
    {
        get;
        private set;
    } = Vector3.up;

    public Vector3 ForwardDirection
    {
        get;
        private set;
    } = Vector3.forward;

    public Vector3 RightDirection
    {
        get;
        private set;
    } = Vector3.right;

    private void Awake()
    {
        GetComponentsIFN();
        rigidbody.sleepThreshold = 0.0f;
        GroundNormal = averageGroundNormal = -GravityDirection;
        ForwardDirection = transform.forward;
    }

    private void Start()
    {
        // https://docs.unity3d.com/Packages/com.unity.inputsystem@1.0/api/UnityEngine.InputSystem.InputActionAsset.html
        // Player/Move
        playerTurnAction = InputSystem.actions.FindAction("351f2ccd-1f9f-44bf-9bec-d62ac5c5f408", true);
        playerAccelerateAction = InputSystem.actions.FindAction("38116054-072b-4018-ae53-07d42a976b11", true);
    }

    private void GetComponentsIFN()
    {
        if (sphereCollider == null)
        {
            sphereCollider = GetComponent<SphereCollider>();
        }

        if (rigidbody == null)
        {
            rigidbody = GetComponent<Rigidbody>();
        }
    }

    private void FixedUpdate()
    {
        Vector3 linearVelocity = rigidbody.linearVelocity;

        // https://docs.unity3d.com/Packages/com.unity.inputsystem@1.8/manual/Workflow-Actions.html
        Vector2 playerMoveInput = new Vector2(playerTurnAction.ReadValue<Vector2>().x,playerAccelerateAction.ReadValue<Vector2>().y);
        float angle = playerMoveInput.x * frontWheelMaxAngle;

        if (IsGrounded)
        {
            // Compute the average ground normal based on our ground contact point(s).
            averageGroundNormal = ComputeAverageGroundNormal(averageGroundNormal);

            // Rotate our ground's normal direction toward the average ground normal.
            GroundNormal = Vector3.RotateTowards(GroundNormal, averageGroundNormal, maxRotRadPerSec * Time.fixedDeltaTime, 0f).normalized;

            // Project our direction's vector onto the ground plane and re-normalized it.
            // https://docs.unity3d.com/2020.3/Documentation//ScriptReference/Vector3.ProjectOnPlane.html
            Vector3 groundProjectedForwardDirection = Vector3.ProjectOnPlane(ForwardDirection, GroundNormal).normalized;

            // Rotate our forward direction vector toward the projected version.
            ForwardDirection = Vector3.RotateTowards(ForwardDirection, groundProjectedForwardDirection, maxRotRadPerSec * Time.fixedDeltaTime, 0f).normalized;

            // Compute the forward component of our velocity.
            float forwardVelocity = Vector3.Dot(ForwardDirection, linearVelocity);

            // Rotate our forward vector based on player's input.
            if (Mathf.Abs(playerMoveInput.x) > 0.01f)
            {
                // Compute how much we can rotate based on our forward velocity.
                float angleOverSpeedRatio = 1.0f;
                float absForwardVelocity = Mathf.Abs(forwardVelocity);

                if (absForwardVelocity < minForwardSpeedForRotation)
                {
                    angleOverSpeedRatio = absForwardVelocity / minForwardSpeedForRotation;
                }

                // Create a Quaternion that will store the rotation we want to apply.
                Quaternion rotation = Quaternion.AngleAxis(angle * angleOverSpeedRatio * Time.fixedDeltaTime, GroundNormal);

                // Apply our rotation to our forward direction vector.
                ForwardDirection = rotation * ForwardDirection;
            }

            // Compute our right vector using our forward and (reversed) ground normal.
            // https://docs.unity3d.com/2020.3/Documentation//ScriptReference/Vector3.Cross.html
            RightDirection = Vector3.Cross(ForwardDirection, -GroundNormal).normalized;

            // Sideways Friction //

            // How much our velocity is slipping velocity?
            // 0    means 100% of our velocity is perpendicular to our forward direction (100% sliping).
            // -1/1 means 0% is perpendicular and thus 100% parallel (no sliping).
            float slip = Vector3.Dot(RightDirection, linearVelocity.normalized);
            float friction = sidewaysFriction.Evaluate(Mathf.Abs(slip));

            // Compute the (inverse) friction force (that prevent our vehicle from sliding).
            float frictionForce = friction * stiffness * -Mathf.Sign(slip);

            // Apply friction force.
            rigidbody.AddForce(frictionForce * RightDirection);

            // Motor Force //
            if (playerMoveInput.y > 0.01f)
            {
                float motorForce = motorPowerOverForwardSpeed.Evaluate(forwardVelocity) * playerMoveInput.y;
                rigidbody.AddForce(motorForce * ForwardDirection);
            }
            // Breaking Force/Motor //
            else
            {
                float breakingForce = 0.0f;
                float breakingDirection = -Mathf.Sign(Vector3.Dot(ForwardDirection, linearVelocity));

                if (playerMoveInput.y < -0.01f)
                {
                    breakingForce = breakForce * Mathf.Abs(playerMoveInput.y);
                }
                else
                {
                    breakingForce = 1.0f + breakForce * 0.1f;
                }

                rigidbody.AddForce(breakingForce * breakingDirection * ForwardDirection);
            }
        }
        // NOT Grounded
        else
        {
            // Reset the average ground normal as the inverse of our gravity direction
            averageGroundNormal = -GravityDirection;

            // Compute how much our linear velocity has "rotated".
            Quaternion rigidbodyRotation = Quaternion.FromToRotation(previousLinearVelocity.normalized, rigidbody.linearVelocity.normalized);

            // Rotate our three main vectors using this rotation.
            GroundNormal = rigidbodyRotation * GroundNormal;
            ForwardDirection = rigidbodyRotation * ForwardDirection;
            RightDirection = rigidbodyRotation * RightDirection;

            // Rotate our forward vector based on player's input.
            if (Mathf.Abs(playerMoveInput.x) > 0.01f)
            {
                // Compute our forward velocity.
                float forwardVelocity = Vector3.Dot(ForwardDirection, linearVelocity);

                // Compute how much we can rotate based on our forward velocity.
                float angleOverSpeedRatio = 1.0f;
                float absForwardVelocity = Mathf.Abs(forwardVelocity);

                if (absForwardVelocity < minForwardSpeedForRotation)
                {
                    angleOverSpeedRatio = absForwardVelocity / minForwardSpeedForRotation;
                }

                // Create a Quaternion that will store the rotation we want to apply.
                Quaternion rotation = Quaternion.AngleAxis(angle * angleOverSpeedRatio * airRotationControl * Time.fixedDeltaTime, GroundNormal);

                // Apply our rotation to our forward direction vector.
                ForwardDirection = rotation * ForwardDirection;

                // Recompute our right vector using our forward and (reversed) ground normal.
                // https://docs.unity3d.com/2020.3/Documentation//ScriptReference/Vector3.Cross.html
                RightDirection = Vector3.Cross(ForwardDirection, -GroundNormal).normalized;
            }
        }

        // Update previous linear velocity.
        previousLinearVelocity = rigidbody.linearVelocity;
    }

    private void Update()
    {
        // Move our model from the starting offset local value.
        modelPivot.position = transform.position + -GroundNormal * sphereCollider.radius;

        // Make our model look forward.
        modelPivot.LookAt(modelPivot.position + ForwardDirection, GroundNormal);

        // https://docs.unity3d.com/Packages/com.unity.inputsystem@1.8/manual/Workflow-Actions.html
        Vector2 playerMoveInput = new Vector2(playerAccelerateAction.ReadValue<Vector2>().x, playerTurnAction.ReadValue<Vector2>().y);
        float wheelAngle = playerMoveInput.x * frontWheelMaxAngle;

        // Update front wheels rotation.
        frontLeftWheel.localEulerAngles = Vector3.up * wheelAngle;
        frontRightWheel.localEulerAngles = Vector3.up * wheelAngle;

        // Update our ground particles (if we have any).
        if (groundParticles != null  && groundParticles.Length > 0)
        {
            for (int index = 0; index < groundParticles.Length; index++)
            {
                groundParticles[index].SetActive(IsGrounded);
            }
        }
    }

    private Vector3 ComputeAverageGroundNormal(Vector3 previousAverageGroundNormal)
    {
        Vector3 averageGroundNormal = Vector3.zero;
        int groundContactCount = 0;

        foreach (List<GroundContactPoint> groundContactPoints in this.groundContactPoints.Values)
        {
            foreach (GroundContactPoint groundContactPoint in groundContactPoints)
            {
                averageGroundNormal += groundContactPoint.Normal;
                groundContactCount++;
            }
        }

        if (groundContactCount == 0)
        {
            return previousAverageGroundNormal;
        }

        if (groundContactCount > 1)
        {
            averageGroundNormal /= (float)groundContactCount;
        }

        return averageGroundNormal;
    }

    private void UpdateGroundContact(Collision collision, bool remove = false)
    {
        Collider collider = collision.collider;

        if (remove)
        {
            groundContactPoints.Remove(collider);

            if (groundContactPoints.Count == 0)
            {
                lastGroundContactPointTime = Time.fixedTime;
            }
        }
        else
        {
            if (!this.groundContactPoints.ContainsKey(collider))
            {
                this.groundContactPoints.Add(collider, new List<GroundContactPoint>(4));
            }
            else
            {
                // Clear the previous ground contact point(s).
                this.groundContactPoints[collider].Clear();
            }

            List<GroundContactPoint> groundContactPoints = this.groundContactPoints[collider];

            // Get the collision's contact point(s).
            int contactCount = collision.contactCount;
            ContactPoint[] contactPoints = new ContactPoint[contactCount];
            collision.GetContacts(contactPoints);

            for (int index = 0; index < contactCount; index++)
            {
                ContactPoint contactPoint = contactPoints[index];

                // Does this contact point should be considered as ground?
                // First let's retrieve the surface normal.
                // N.b.: ContactPoint.normal represent the "average" normal between the two collider's surface's normal.

                // https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Collider.Raycast.html
                Ray ray = new(transform.position, (contactPoint.point - transform.position).normalized);
                collider.Raycast(ray, out RaycastHit hitInfo, float.MaxValue);

                // Does this contact point should be considered as ground?
                // https://docs.unity3d.com/2020.3/Documentation//ScriptReference/Vector3.Dot.html
                bool contactPointIsGround = Vector3.Dot(hitInfo.normal, GravityDirection) < gravityGroundThreshold;
                if (contactPointIsGround)
                {
                    // Ok! Let's create a GroundContactPoint and add it to our list.
                    GroundContactPoint groundContactPoint = new(contactPoint.point, hitInfo.normal);
                    groundContactPoints.Add(groundContactPoint);
                }
            }
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        UpdateGroundContact(collision);
    }

    private void OnCollisionStay(Collision collision)
    {
        UpdateGroundContact(collision);
    }

    private void OnCollisionExit(Collision collision)
    {
        UpdateGroundContact(collision, true);
    }

#if UNITY_EDITOR

    private void OnDrawGizmos()
    {
        GetComponentsIFN();

        // Sphere Collider //
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, sphereCollider.radius);

        // Forward Direction //
        Gizmos.color = Color.blue;
        if (Application.isPlaying)
        {
            Gizmos.DrawLine(transform.position, transform.position + ForwardDirection);
        }
        else
        {
            Gizmos.DrawLine(transform.position, transform.position + transform.forward);
        }

        // Right Direction //
        Gizmos.color = Color.red;
        if (Application.isPlaying)
        {
            Gizmos.DrawLine(transform.position, transform.position + RightDirection);
        }
        else
        {
            Gizmos.DrawLine(transform.position, transform.position + transform.right);
        }

        // Linear Velocity //
        Gizmos.color = Color.green;
        Gizmos.DrawLine(transform.position, transform.position + rigidbody.linearVelocity);

        // Ground //
        if (IsGrounded)
        {
            // Ground Contact Point(s) //
            Gizmos.color = Color.cyan;
            foreach (var groundContactPoints in groundContactPoints.Values)
            {
                foreach (var groundContactPoint in groundContactPoints)
                {
                    // Ground Contact Point //
                    Gizmos.DrawWireSphere(groundContactPoint.Point, 0.1f);

                    // Ground Contact Normal //
                    Gizmos.DrawLine(groundContactPoint.Point, groundContactPoint.Point + groundContactPoint.Normal * 0.2f);
                }
            }
        }
    }

#endif

    private struct GroundContactPoint
    {
        public readonly Vector3 Point;
        public readonly Vector3 Normal;

        public GroundContactPoint(Vector3 point, Vector3 normal)
        {
            Point = point;
            Normal = normal;
        }
    }

}
