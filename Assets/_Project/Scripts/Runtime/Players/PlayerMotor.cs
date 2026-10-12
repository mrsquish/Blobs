using Blobs.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Blobs.Players
{
    /// <summary>
    /// Camera-relative movement, jumping and interaction for one player, driven by that player's own
    /// PlayerInput actions (each PlayerInput gets its own copy of the action asset).
    /// </summary>
    [RequireComponent(typeof(CharacterController), typeof(CoopPlayer))]
    public class PlayerMotor : MonoBehaviour
    {
        [SerializeField] float _moveSpeed = 6f;
        [SerializeField] float _turnSpeed = 720f;
        [SerializeField] float _jumpHeight = 1.4f;
        [SerializeField] float _gravity = -25f;
        [SerializeField] float _interactRadius = 1.75f;
        [SerializeField] LayerMask _interactMask = ~0;

        readonly Collider[] _interactHits = new Collider[8];

        CharacterController _controller;
        CoopPlayer _player;
        InputAction _move;
        InputAction _jump;
        InputAction _interact;
        Transform _camera;
        float _verticalVelocity;

        void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _player = GetComponent<CoopPlayer>();

            var actions = GetComponent<PlayerInput>().actions;
            _move = actions.FindAction(InputNames.Move, throwIfNotFound: true);
            _jump = actions.FindAction(InputNames.Jump, throwIfNotFound: true);
            _interact = actions.FindAction(InputNames.Interact, throwIfNotFound: true);
        }

        void Update()
        {
            // While researching the Player map is disabled, but the body still needs gravity.
            var exploring = !_player.IsResearching;
            var input = exploring ? _move.ReadValue<Vector2>() : Vector2.zero;
            var planar = CameraRelative(input) * _moveSpeed;

            if (_controller.isGrounded && _verticalVelocity < 0f)
                _verticalVelocity = -2f;
            if (exploring && _controller.isGrounded && _jump.WasPressedThisFrame())
                _verticalVelocity = Mathf.Sqrt(_jumpHeight * -2f * _gravity);
            _verticalVelocity += _gravity * Time.deltaTime;

            _controller.Move((planar + Vector3.up * _verticalVelocity) * Time.deltaTime);

            if (planar.sqrMagnitude > 0.01f)
            {
                var target = Quaternion.LookRotation(planar, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, target, _turnSpeed * Time.deltaTime);
            }

            if (exploring && _interact.WasPressedThisFrame())
                TryInteract();
        }

        /// <summary>
        /// Moves the player without the CharacterController fighting the change. Safe to call before Awake
        /// (CoopSession places players from onPlayerJoined, which fires mid-instantiation).
        /// </summary>
        public void Teleport(Vector3 position, Quaternion rotation)
        {
            var controller = GetComponent<CharacterController>();
            controller.enabled = false;
            transform.SetPositionAndRotation(position, rotation);
            controller.enabled = true;
            _verticalVelocity = 0f;
        }

        Vector3 CameraRelative(Vector2 input)
        {
            if (_camera == null && Camera.main != null)
                _camera = Camera.main.transform;
            if (_camera == null)
                return new Vector3(input.x, 0f, input.y);

            // Everyone shares one camera, so "up on the stick" means "away from the camera" for all players.
            var forward = Vector3.ProjectOnPlane(_camera.forward, Vector3.up).normalized;
            var right = Vector3.ProjectOnPlane(_camera.right, Vector3.up).normalized;
            return Vector3.ClampMagnitude(forward * input.y + right * input.x, 1f);
        }

        void TryInteract()
        {
            var count = Physics.OverlapSphereNonAlloc(transform.position, _interactRadius, _interactHits,
                _interactMask, QueryTriggerInteraction.Collide);

            IInteractable closest = null;
            var closestDistance = float.MaxValue;
            for (var i = 0; i < count; i++)
            {
                var interactable = _interactHits[i].GetComponentInParent<IInteractable>();
                if (interactable == null) continue;

                var distance = (_interactHits[i].ClosestPoint(transform.position) - transform.position).sqrMagnitude;
                if (distance >= closestDistance) continue;
                closest = interactable;
                closestDistance = distance;
            }

            closest?.TryInteract(_player);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, _interactRadius);
        }
    }
}
