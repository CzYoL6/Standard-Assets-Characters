using System;
using StandardAssets.Characters.Attributes;
using StandardAssets.Characters.Effects;
using StandardAssets.Characters.Physics;
using UnityEngine;
using UnityPhysics = UnityEngine.Physics;

namespace StandardAssets.Characters.Common
{
	/// <summary>
	/// Abstract bass class for character brains
	/// </summary>
	public abstract class CharacterBrain : MonoBehaviour
	{
		public Action<MovementZoneId?> changeMovementZone;
		
		[Header("Controllers")]
		[SerializeField, Tooltip("Settings for the default CharacterController.")]
		protected CharacterControllerAdapter characterControllerAdapter;

		[SerializeField, Tooltip("Settings for the OpenCharacterController.")]
		protected OpenCharacterControllerAdapter openCharacterControllerAdapter;

		/// <summary>
		/// Gets/sets the controller adapter implementation used by the Character
		/// </summary>
		public ControllerAdapter controllerAdapter { get; protected set; }

		/// <summary>
		/// Gets/sets the planar speed (i.e. ignoring the displacement) of the CharacterBrain
		/// </summary>
		public float planarSpeed { get; protected set; }
		
		private Vector3 lastPosition;

		public abstract float normalizedForwardSpeed { get;}

		public abstract float targetYRotation { get; set; }

		/// <summary>
		/// Get controller adapters and input on Awake
		/// </summary>
		protected virtual void Awake()
		{
			if (GetComponent<CharacterController>() != null)
			{
				controllerAdapter = characterControllerAdapter;
			}
			else if (GetComponent<OpenCharacterController>() != null)
			{
				controllerAdapter = openCharacterControllerAdapter;
			}
			else
			{
				Debug.LogErrorFormat("{0} must have a CharacterController or OpenCharacterController attached.",
					name);
			}
			
			lastPosition = transform.position;

			if (controllerAdapter != null)
			{
				controllerAdapter.Awake(transform);
			}
		}

		/// <summary>
		/// Calculates the planarSpeed of the CharacterBrain
		/// </summary>
		protected virtual void Update()
		{
			Vector3 newPosition = transform.position;
			newPosition.y = 0f;
			float displacement = (lastPosition - newPosition).magnitude;
			planarSpeed = displacement / Time.deltaTime;
			lastPosition = newPosition;
		}

		public void ChangeMovementZone(MovementZoneId? zoneId)
		{
			if (changeMovementZone != null)
			{
				changeMovementZone(zoneId);
			}
		}
	}
	
	/// <summary>
	/// Abstract wrapper for character controllers. Requires a <see cref="CharacterInput"/> and <see cref="CharacterBrain"/>.
	/// </summary>
	[Serializable]
	public abstract class ControllerAdapter
	{
		/// <summary>
		/// Used as a clamp for downward velocity.
		/// </summary>
		[SerializeField, Tooltip("The maximum speed that the character can move downwards")]
		protected float terminalVelocity = 10f;

		[SerializeField, Tooltip("Gravity scale applied during a jump")]
		protected AnimationCurve jumpGravityMultiplierAsAFactorOfForwardSpeed =
			AnimationCurve.Constant(0.0f, 1.0f, 1.0f);

		[SerializeField, Tooltip("Gravity scale applied during a fall")]
		protected AnimationCurve fallGravityMultiplierAsAFactorOfForwardSpeed =
			AnimationCurve.Constant(0.0f, 1.0f, 1.0f);

		[SerializeField, Tooltip("Gravity scale applied during a jump without jump button held")]
		protected AnimationCurve minJumpHeightMultiplierAsAFactorOfForwardSpeed =
			AnimationCurve.Constant(0.0f, 1.0f, 1.0f);

		[SerializeField, Tooltip("The speed at which gravity is allowed to change")]
		protected float gravityChangeSpeed = 10f;

		/// <inheritdoc/>
		public bool isGrounded { get; private set; }

		/// <inheritdoc/>
		public abstract bool startedSlide { get; }

		/// <inheritdoc/>
		public event Action landed;

		/// <inheritdoc/>
		public event Action jumpVelocitySet;

		/// <inheritdoc/>
		public event Action<float> startedFalling;

		/// <inheritdoc/>
		public float airTime { get; private set; }

		/// <inheritdoc/>
		public float fallTime { get; private set; }

		/// <summary>
		/// Gets the radius of the character.
		/// </summary>
		/// <value>The radius used for predicting the landing position.</value>
		protected abstract float radius { get; }

		/// <summary>
		/// Gets the character's world foot position.
		/// </summary>
		/// <value>A world position at the bottom of the character</value>
		protected abstract Vector3 footWorldPosition { get; }

		/// <summary>
		/// Gets the collision layer mask used for physics grounding,
		/// </summary>
		protected abstract LayerMask collisionLayerMask { get; }

		// the number of time sets used for trajectory prediction.
		private const int k_TrajectorySteps = 60;

		// the time step used between physics trajectory steps.
		private const float k_TrajectoryPredictionTimeStep = 0.016f;

		// colliders used in physics checks for landing prediction
		private readonly Collider[] trajectoryPredictionColliders = new Collider[1];

		// the current value of gravity
		private float gravity;

		// the source of the normalized forward speed.
		private CharacterBrain characterBrain;

		/// <summary>
		/// The predicted landing position of the character. Null if a position could not be predicted.
		/// </summary>
		protected Vector3? predictedLandingPosition;
#if UNITY_EDITOR
		private readonly Vector3[] jumpSteps = new Vector3[k_TrajectorySteps];
		private int jumpStepCount;
#endif
		
		/// <summary>
		/// Reference to the transform of the game object, on which this class will do work.
		/// </summary>
		public Transform cachedTransform { get; private set; }

		/// <inheritdoc/>
		public float normalizedVerticalSpeed
		{
			get;
			private set;
		}

		/// <summary>
		/// The initial jump velocity.
		/// </summary>
		/// <value>Velocity used to initiate a jump.</value>
		protected float initialJumpVelocity;

		/// <summary>
		/// The current vertical velocity.
		/// </summary>
		/// <value>Calculated using <see cref="initialJumpVelocity"/>, <see cref="airTime"/> and
		/// <see cref="CalculateGravity"/></value>
		protected float currentVerticalVelocity;

		/// <summary>
		/// The last used ground (vertical velocity excluded ie 0) velocity.
		/// </summary>
		/// <value>Velocity based on the moveVector used by <see cref="Move"/>.</value>
		private Vector3 cachedGroundVelocity;

		/// <summary>
		/// The current vertical vector.
		/// </summary>
		/// <value><see cref="Vector3.zero"/> with a y based on <see cref="currentVerticalVelocity"/>.</value>
		private Vector3 verticalVector = Vector3.zero;

		private CharacterInput characterInput;

		/// <summary>
		/// Gets the current jump gravity multiplier as a factor of normalized forward speed.
		/// </summary>
		private float jumpGravityMultiplier
		{
			get
			{
				return jumpGravityMultiplierAsAFactorOfForwardSpeed.Evaluate(
					characterBrain.normalizedForwardSpeed);
			}
		}
		
		/// <summary>
		/// Gets the current minimum jump height gravity multiplier as a factor of normalized forward speed.
		/// </summary>
		private float minJumpHeightMultiplier
		{
			get
			{
				return minJumpHeightMultiplierAsAFactorOfForwardSpeed.Evaluate(
					characterBrain.normalizedForwardSpeed);
			}
		}
		
		/// <summary>
		/// Gets the current fall gravity multiplier as a factor of normalized forward speed.
		/// </summary>
		private float fallGravityMultiplier
		{
			get
			{
				return fallGravityMultiplierAsAFactorOfForwardSpeed.Evaluate(
					characterBrain.normalizedForwardSpeed);
			}
		}

		/// <inheritdoc />
		public void Move(Vector3 moveVector, float deltaTime)
		{
			isGrounded = CheckGrounded();
			AerialMovement(deltaTime);
			MoveCharacter(moveVector + verticalVector);
			cachedGroundVelocity = moveVector / deltaTime;
		}

		/// <summary>
		/// Calculates the current predicted fall distance based on the predicted landing position
		/// </summary>
		/// <returns>The predicted fall distance</returns>
		public float GetPredictedFallDistance()
		{
			UpdatePredictedLandingPosition();
			return predictedLandingPosition == null
				? float.MaxValue
				: footWorldPosition.y - ((Vector3) predictedLandingPosition).y;
		}

		/// <summary>
		/// Tries to jump.
		/// </summary>
		/// <param name="initialVelocity"></param>
		public void SetJumpVelocity(float initialVelocity)
		{
			currentVerticalVelocity = initialJumpVelocity = initialVelocity;
			if (jumpVelocitySet != null)
			{
				jumpVelocitySet();
			}
		}

		/// <summary>
		/// Initialization on load. Must be manually called.
		/// </summary>
		/// <param name="transform">Transform of the game object, on which this class will do work.</param>
		public virtual void Awake(Transform transform)
		{
			cachedTransform = transform;
			normalizedVerticalSpeed = 0.0f;
			characterInput = cachedTransform.GetComponent<CharacterInput>();
			characterBrain = cachedTransform.GetComponent<CharacterBrain>();

			if (terminalVelocity > 0.0f)
			{
				terminalVelocity = -terminalVelocity;
			}

			gravity = UnityPhysics.gravity.y;
		}

		/// <summary>
		/// Updates the predicted landing position by stepping through the fall trajectory
		/// </summary>
		private void UpdatePredictedLandingPosition()
		{
			Vector3 currentPosition = footWorldPosition;
			Vector3 moveVector = cachedGroundVelocity;
			float currentAirTime = 0.0f;
			for (int i = 0; i < k_TrajectorySteps; i++)
			{
				moveVector.y = Mathf.Clamp(gravity * fallGravityMultiplier * currentAirTime,  terminalVelocity, 
				                           Mathf.Infinity);
				currentPosition += moveVector * k_TrajectoryPredictionTimeStep;
				currentAirTime += k_TrajectoryPredictionTimeStep;
#if UNITY_EDITOR
				jumpSteps[i] = currentPosition;
#endif
				if (IsGroundCollision(currentPosition))
				{
#if UNITY_EDITOR
					// for gizmos
					jumpStepCount = i;
#endif
					predictedLandingPosition = currentPosition;
					return;
				}
			}
#if UNITY_EDITOR
			jumpStepCount = k_TrajectorySteps;
#endif
			predictedLandingPosition = null;
		}

		/// <summary>
		/// Checks if the given position would collide with the ground collision layer.
		/// </summary>
		/// <param name="position">Position to check</param>
		/// <returns>True if a ground collision would occur at the given position.</returns>
		private bool IsGroundCollision(Vector3 position)
		{
			// move sphere but to match bottom of character's capsule collider
			int colliderCount = UnityPhysics.OverlapSphereNonAlloc(position + new Vector3(0.0f, radius, 0.0f),
																   radius, trajectoryPredictionColliders,
																   collisionLayerMask);
			return colliderCount > 0.0f;
		}

		/// <summary>
		/// Handles Jumping and Falling
		/// </summary>
		private void AerialMovement(float deltaTime)
		{
			airTime += deltaTime;
			CalculateGravity(deltaTime);
			if (currentVerticalVelocity >= 0.0f)
			{
				currentVerticalVelocity = Mathf.Clamp(initialJumpVelocity + gravity * airTime, terminalVelocity,
													  Mathf.Infinity);
			}
			
			float previousFallTime = fallTime;

			if (currentVerticalVelocity < 0.0f)
			{
				currentVerticalVelocity = Mathf.Clamp(gravity * fallTime, terminalVelocity, Mathf.Infinity);
				fallTime += deltaTime;
				if (isGrounded)
				{
					initialJumpVelocity = 0.0f;
					verticalVector = Vector3.zero;

					//Play the moment that the character lands and only at that moment
					if (Math.Abs(airTime - deltaTime) > Mathf.Epsilon && landed != null)
					{
						landed();
					}

					fallTime = 0.0f;
					airTime = 0.0f;
					return;
				}
			}

			if (Mathf.Approximately(previousFallTime, 0.0f) && fallTime > Mathf.Epsilon)
			{
				if (startedFalling != null)
				{
					startedFalling(GetPredictedFallDistance());
				}
			}
			verticalVector = new Vector3(0.0f, currentVerticalVelocity * deltaTime, 0.0f);
		}

		/// <summary>
		/// Calculates the current gravity modified based on current vertical velocity
		/// </summary>
		private void CalculateGravity(float deltaTime)
		{
			float gravityFactor;
			if (currentVerticalVelocity < 0.0f)
			{
				gravityFactor = fallGravityMultiplier;
				if (initialJumpVelocity < Mathf.Epsilon)
				{
					normalizedVerticalSpeed = 0.0f;
				}
				else
				{
					normalizedVerticalSpeed = Mathf.Clamp(currentVerticalVelocity / 
					                                      (initialJumpVelocity * gravityFactor), -1.0f, 1.0f);
				}
			}
			else
			{
				gravityFactor = jumpGravityMultiplier;
				if (characterInput  != null && !characterInput.hasJumpInput) // if no input apply min jump modifier
				{
					gravityFactor *= minJumpHeightMultiplier;
				}
				normalizedVerticalSpeed = initialJumpVelocity > 0.0f ? currentVerticalVelocity / initialJumpVelocity 
																	   : 0.0f;
			}

			float newGravity = gravityFactor * UnityPhysics.gravity.y;
			gravity = Mathf.Lerp(gravity, newGravity, deltaTime * gravityChangeSpeed);
		}

		/// <returns>True if the character is grounded; false otherwise.</returns>
		protected abstract bool CheckGrounded();

		/// <summary>
		/// Moves the character by <paramref name="movement"/> world units.
		/// </summary>
		/// <param name="movement">The value to move the character by in world units.</param>
		protected abstract void MoveCharacter(Vector3 movement);

#if UNITY_EDITOR
		/// <summary>
		/// Draws gizmos. Must be manually called.
		/// </summary>
		public virtual void OnDrawGizmosSelected()
		{
			for (int index = 0; index < jumpStepCount - 1; index++)
			{
				Gizmos.DrawLine(jumpSteps[index], jumpSteps[index + 1]);
			}

			Gizmos.color = Color.green;
			if (predictedLandingPosition != null)
			{
				Gizmos.DrawSphere((Vector3) predictedLandingPosition, 0.05f);
			}
		}
#endif
	}
	
	/// <summary>
	/// A controller adapter implementation that uses the default Unity character controller.
	/// </summary>
	[Serializable]
	public class CharacterControllerAdapter : ControllerAdapter
	{
		/// <summary>
		/// The distance used to check if grounded
		/// </summary>
		[SerializeField]
		protected float groundCheckDistance = 0.55f;

		/// <summary>
		/// Layers to use in the ground check
		/// </summary>
		[Tooltip("Layers to use in the ground check")]
		[SerializeField]
		protected LayerMask groundCheckMask;

		/// <summary>
		/// Character controller
		/// </summary>
		private CharacterController characterController;

		public override bool startedSlide
		{
			// CharacterController will never slide down a slope
			get { return false; }
		}

		protected override float radius
		{
			get { return characterController.radius; }
		}

		protected override Vector3 footWorldPosition
		{
			get
			{
				return cachedTransform.position + characterController.center -
					   new Vector3(0, characterController.height * 0.5f);
			}
		}

		protected override LayerMask collisionLayerMask
		{
			get { return groundCheckMask; }
		}

		public override void Awake(Transform transform)
		{
			//Gets the attached character controller
			characterController = transform.GetComponent<CharacterController>();
			base.Awake(transform);
		}

		/// <summary>
		/// Checks character controller grounding
		/// </summary>
		protected override bool CheckGrounded()
		{
			Debug.DrawRay(cachedTransform.position + characterController.center, 
						  new Vector3(0,-groundCheckDistance * characterController.height,0), Color.red);
			if (UnityEngine.Physics.Raycast(cachedTransform.position + characterController.center, 
				-cachedTransform.up, groundCheckDistance * characterController.height, groundCheckMask))
			{
				return true;
			}
			return CheckEdgeGrounded();
			
		}

		protected override void MoveCharacter(Vector3 movement)
		{
			characterController.Move(movement);
		}

		/// <summary>
		/// Checks character controller edges for ground
		/// </summary>
		private bool CheckEdgeGrounded()
		{
			
			Vector3 xRayOffset = new Vector3(characterController.radius,0f,0f);
			Vector3 zRayOffset = new Vector3(0f,0f,characterController.radius);		
			
			for (int i = 0; i < 4; i++)
			{
				float sign = 1f;
				Vector3 rayOffset;
				if (i % 2 == 0)
				{
					rayOffset = xRayOffset;
					sign = i - 1f;
				}
				else
				{
					rayOffset = zRayOffset;
					sign = i - 2f;
				}
				Debug.DrawRay(cachedTransform.position + characterController.center + sign * rayOffset, 
					new Vector3(0,-groundCheckDistance * characterController.height,0), Color.blue);

				if (UnityEngine.Physics.Raycast(cachedTransform.position + characterController.center + sign * rayOffset,
					-cachedTransform.up,groundCheckDistance * characterController.height, groundCheckMask))
				{
					return true;
				}
			}
			return false;
		}
	}
	
	/// <summary>
	/// A controller adapter implementation that uses <see cref="OpenCharacterController"/>.
	/// </summary>
	[Serializable]
	public class OpenCharacterControllerAdapter : ControllerAdapter
	{
		[SerializeField, Tooltip("Reference to the attached OpenCharacterController.")]
		protected OpenCharacterController characterController;

		/// <summary>
		/// Gets the open character controller.
		/// </summary>
		/// <value>The class that handles physics of the character.</value>
		public OpenCharacterController openCharacterController
		{
			get { return characterController; }
		}
		
		/// <inheritdoc/>
		public override bool startedSlide
		{
			get { return characterController.startedSlide; }
		}

		/// <inheritdoc/>
		protected override Vector3 footWorldPosition
		{
			get { return characterController.GetFootWorldPosition(); }
		}
		
		/// <inheritdoc/>
		protected override LayerMask collisionLayerMask
		{
			get { return characterController.GetCollisionLayerMask(); }
		}

		/// <inheritdoc/>
		protected override float radius
		{
			get { return characterController.scaledRadius + characterController.GetSkinWidth(); }
		}
		
		/// <inheritdoc/>
		protected override bool CheckGrounded()
		{
			return characterController.isGrounded;
		}

		/// <inheritdoc/>
		protected override void MoveCharacter(Vector3 movement)
		{
			CollisionFlags collisionFlags = characterController.Move(movement);
			if ((collisionFlags & CollisionFlags.CollidedAbove) == CollisionFlags.CollidedAbove)
			{
				currentVerticalVelocity = 0f;
				initialJumpVelocity = 0f;
			}
		}

		/// <summary>
		/// Get the OpenCharacterController.
		/// </summary>
		public override void Awake(Transform transform)
		{
			base.Awake(transform);
			if (characterController == null)
			{
				characterController = transform.GetComponent<OpenCharacterController>();
			}
		}
	}
}