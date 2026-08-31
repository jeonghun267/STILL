using System.Linq;
using UnityEngine;


public class FreeSwimming : MonoBehaviour
{
  public float maxSpeed = 5f;
    public float minSpeed = 1f;
    public float rotationSpeed = 2f;
    public float avoidanceDistance = 5f;
    public LayerMask obstacleLayer;
    public float targetProximityThreshold = 1f;
    public float directionChangeInterval = 2f; // 추가: 방향 전환 간격

    private Vector3 _targetPosition;
    private Collider[] _obstacles;
    private float _speed;
    private float _directionChangeTimer;

    private void Start()
    {
        SetRandomTargetPosition();
        _speed = Random.Range(minSpeed, maxSpeed); // 속도에 랜덤 요소 추가
        _directionChangeTimer = directionChangeInterval;
    }

    private void Update()
    {
        MoveTowardsTarget();
        HandleDirectionChange();
    }

    private void SetRandomTargetPosition()
    {
        var angle = Random.Range(0f, 2f * Mathf.PI);
        var radius = Random.Range(0f, SeaControl.Instance.radius);
        var x = radius * Mathf.Cos(angle);
        var z = radius * Mathf.Sin(angle);
        var y = Random.Range(-SeaControl.Instance.height / 2, SeaControl.Instance.height / 2);

        _targetPosition = SeaControl.Instance.transform.position + new Vector3(x, y, z);
    }

    private void MoveTowardsTarget()
    {
        _obstacles = Physics.OverlapSphere(transform.position, avoidanceDistance, obstacleLayer);

        if (_obstacles.Length > 0)
        {
            var avoidanceDirection = _obstacles.Aggregate(Vector3.zero, (current, obstacle) => current + (transform.position - obstacle.transform.position).normalized);

            var avoidanceRotation = Quaternion.LookRotation(avoidanceDirection);
            transform.rotation = Quaternion.Slerp(transform.rotation, avoidanceRotation, rotationSpeed * Time.deltaTime);
        }
        else
        {
            var direction = _targetPosition - transform.position;
            Quaternion targetRotation = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);

            var distanceToTarget = direction.magnitude;
            var speedMultiplier = Mathf.Clamp01(distanceToTarget / avoidanceDistance); 
            _speed = Mathf.Lerp(minSpeed, maxSpeed, speedMultiplier); // 속도에 랜덤 요소 추가
            transform.Translate(Vector3.forward * (_speed * Time.deltaTime));
        }

        if (Vector3.Distance(transform.position, _targetPosition) <= targetProximityThreshold)
        {
            SetRandomTargetPosition();
        }
    }

    private void HandleDirectionChange()
    {
        _directionChangeTimer -= Time.deltaTime;
        if (_directionChangeTimer <= 0f)
        {
            SetRandomTargetPosition();
            _directionChangeTimer = directionChangeInterval;
        }
    }

    // private void OnDrawGizmosSelected()
    // {
    //     Gizmos.color = Color.yellow;
    //     Gizmos.DrawWireSphere(transform.position, avoidanceDistance);
    // }
}
