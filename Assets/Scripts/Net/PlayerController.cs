using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RouletteParty.Net
{
    /// <summary>
    /// 소유자 권위(owner-authoritative) 이동.
    /// IsOwner 인 로컬 플레이어만 입력을 읽어 CharacterController 로 자기 캐릭터를 이동시킨다.
    /// 실제 위치/회전 복제는 같은 프리팹에 붙은 NetworkTransform 이 담당한다.
    ///  - ClientNetworkTransform 컴포넌트를 쓰거나(기본 권장),
    ///  - 혹은 기본 NetworkTransform 의 Authority Mode 를 Owner 로 설정.
    ///
    /// 카메라: 씬의 Main Camera(태그 MainCamera) 하나를 소유자만 붙잡아 따라간다.
    /// (카메라는 프리팹 안에 넣지 말 것. 넣으면 원격 플레이어마다 카메라가 생겨 충돌한다.)
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : NetworkBehaviour
    {
        [Header("이동")]
        [SerializeField] private float _moveSpeed = 5f;
        [SerializeField] private float _rotationSpeed = 720f;
        [SerializeField] private float _gravity = -20f;
        [SerializeField] private float _jumpHeight = 1.4f;

        [Header("카메라 (소유자 전용)")]
        [SerializeField] private bool _followWithMainCamera = true;
        [SerializeField] private Vector3 _cameraOffset = new Vector3(0f, 8f, -8f);
        [SerializeField] private float _cameraFollowSpeed = 10f;
        [SerializeField] private float _cameraLookHeight = 1f;

        private CharacterController _controller;
        private Camera _mainCamera;
        private float _verticalVelocity;

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
        }

        public override void OnNetworkSpawn()
        {
            // 소유권 정보는 스폰 이후에 확정되므로 여기서 분기한다.
            if (!IsOwner)
            {
                // 비소유자 인스턴스는 입력/이동/카메라를 하지 않는다.
                // CharacterController 도 꺼서, NetworkTransform 이 transform.position 을 직접 쓸 때
                // CharacterController 의 내부 충돌 처리와 간섭하지 않게 한다.
                // (위치는 NetworkTransform 이 받아서 복제해 준다.)
                if (_controller != null) _controller.enabled = false;
                enabled = false;
                return;
            }

            if (_followWithMainCamera)
            {
                _mainCamera = Camera.main;
                if (_mainCamera != null) SnapCamera();
            }
        }

        private void Update()
        {
            if (!IsOwner) return; // 방어적 게이트

            var kb = Keyboard.current;
            if (kb == null) return; // 키보드 디바이스 없음/포커스 없음일 때 안전 처리

            // --- WASD 입력 (월드 공간) ---
            float x = 0f, z = 0f;
            if (kb.aKey.isPressed) x -= 1f;
            if (kb.dKey.isPressed) x += 1f;
            if (kb.wKey.isPressed) z += 1f;
            if (kb.sKey.isPressed) z -= 1f;

            Vector3 input = new Vector3(x, 0f, z);
            if (input.sqrMagnitude > 1f) input.Normalize();

            // --- 중력 & 점프 ---
            if (_controller.isGrounded && _verticalVelocity < 0f)
                _verticalVelocity = -2f; // 지면에 살짝 눌러 붙임

            if (_controller.isGrounded && kb.spaceKey.wasPressedThisFrame)
                _verticalVelocity = Mathf.Sqrt(_jumpHeight * -2f * _gravity);

            _verticalVelocity += _gravity * Time.deltaTime;

            // --- 이동 적용 (Owner 가 transform 을 바꾸면 NetworkTransform 이 복제) ---
            Vector3 horizontal = input * _moveSpeed;
            Vector3 velocity = new Vector3(horizontal.x, _verticalVelocity, horizontal.z);
            _controller.Move(velocity * Time.deltaTime);

            // --- 이동 방향으로 회전 ---
            if (input.sqrMagnitude > 0.001f)
            {
                Quaternion target = Quaternion.LookRotation(new Vector3(input.x, 0f, input.z));
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation, target, _rotationSpeed * Time.deltaTime);
            }
        }

        private void LateUpdate()
        {
            if (!IsOwner || !_followWithMainCamera) return;

            if (_mainCamera == null)
            {
                // 카메라가 늦게 생성된 경우 대비해 다시 시도
                _mainCamera = Camera.main;
                if (_mainCamera == null) return;
                SnapCamera();
                return;
            }

            Vector3 desired = transform.position + _cameraOffset;
            _mainCamera.transform.position = Vector3.Lerp(
                _mainCamera.transform.position, desired, _cameraFollowSpeed * Time.deltaTime);
            _mainCamera.transform.LookAt(transform.position + Vector3.up * _cameraLookHeight);
        }

        private void SnapCamera()
        {
            _mainCamera.transform.position = transform.position + _cameraOffset;
            _mainCamera.transform.LookAt(transform.position + Vector3.up * _cameraLookHeight);
        }
    }
}
