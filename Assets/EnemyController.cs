using System;
using UnityEngine;
using Random = System.Random;

public class EnemyController : MonoBehaviour {
    public enum EnemyState {
        Roaming,
        Turning,
        AbleToPush,
        Pushing,
        BeingPushed,
        Chasing,
        JumpingDown,
        ChasingAlly,
        SearchingAlly,
        CommsWithAllySelfInitiated,
        CommsWithAllyOtherInitiated,
        CommsWithAllyManipulatorInitiated,
        Boosting,
        BeingBoosted,
        ReturnToHoleBoosting,
        ReturnToHoleBeingBoosted,
    }

    public enum BoostStatus {
        Boosting,
        BeingBoosted,
        Rejection,
        Waiting,
        Undefined
    }

    public EnemyState state;
    public BoostStatus boostStatus;
    public BoostStatus allyBoostStatus;
    public float playerDetectionRange;
    public float pushRange;
    public float holeDetectionRange;
    public int currentFloor;
    public bool isNearHole;
    public int intersectionRadius;
    public GameObject nearestHole;

    private GameObject _player;
    private GameObject _target;
    private Random _rand;
    private bool _isAtIntersection;
    private bool _isBeingPushed;
    private bool _isReceivingComms;
    private bool _isReceivingManipulatorComms;
    private bool _hasResponded;
    private bool _wasBoostSuccessful;
    private bool _isPushing;
    private float _allyWillingnessToBoost;
    private float _turnTimer;
    private float _pushTimer;

    private void Start() {
        _rand = new Random();
    }

    private void Update() {
        _player = PlayerManager.Instance.player;
        var playerPos = _player.transform.position;
        var playerDist = Vector3.Distance(playerPos, transform.position);

        var shortestHoleDist = holeDetectionRange + 1;
        isNearHole = false;
        nearestHole = null;
        foreach (var hole in HoleManager.Instance.holes) {
            var holeCollider = hole.GetComponent<HoleCollider>();
            var holePos = holeCollider.transform.position;
            var holeDist = Vector3.Distance(holePos, transform.position);
            if (holeDist < shortestHoleDist && holeDist < holeDetectionRange) {
                shortestHoleDist = holeDist;
                isNearHole = true;
                nearestHole = hole;
            }
        }

        switch (state) {
            case EnemyState.Roaming: {
                // Choose target some distance ahead
                // Navigate to target with navmesh
                
                var canPush = false;
                foreach (var ally in EnemyManager.Instance.enemies) {
                    if (ally.GetInstanceID() != GetInstanceID()) {
                        var allyPos = ally.transform.position;
                        var allyDist = Vector3.Distance(allyPos, transform.position);
                        // Create re-usable interfaces for controllers for other AI implementations
                        var allyController = ally.GetComponent<EnemyController>();
                        if (allyDist < pushRange && allyController.currentFloor == currentFloor) {
                            var hole = allyController.nearestHole;
                            if (allyController.isNearHole && hole != null) {
                                var allyDirection = Vector3.Angle(allyPos, transform.position);
                                var holeDirection = Vector3.Angle(hole.transform.position, allyPos);
                                
                                if (Math.Abs(allyDirection - holeDirection) < 5) {
                                    canPush = true;
                                    _target = ally;
                                }
                            }
                        }
                    }
                }
                // Ally is within certain range of us
                // Ally is within certain range of hole
                // Angle between ally, hole, and us is a straight line
                
                if (_isBeingPushed) {
                    state = EnemyState.BeingPushed;
                } else if (_isAtIntersection) {
                    if (_rand.NextDouble() > 0.5 || isWallInFront()) {
                        state = EnemyState.Turning;
                    }
                } else if (playerDist < playerDetectionRange && _player.GetComponent<PlayerController>().currentFloor == currentFloor) {
                    _target = _player;
                    state = EnemyState.Chasing;
                } else if (_isReceivingComms) {
                    state = EnemyState.CommsWithAllyOtherInitiated;
                } else if (_isReceivingManipulatorComms) {
                    state = EnemyState.CommsWithAllyManipulatorInitiated;
                } else if (canPush) {
                    state = EnemyState.AbleToPush;
                }
                break;
            }
            
            case EnemyState.Turning: {
                if (Time.time > _turnTimer) {
                    if (_rand.Next(0, 1) == 0) {
                        transform.rotation = Quaternion.Euler(new Vector3(transform.rotation.x, transform.rotation.y + 90, transform.rotation.z));
                        if (isWallInFront()) {
                            transform.rotation = Quaternion.Euler(new Vector3(transform.rotation.x, transform.rotation.y + 180, transform.rotation.z));
                        }
                    } else {
                        transform.rotation = Quaternion.Euler(new Vector3(transform.rotation.x, transform.rotation.y - 90, transform.rotation.z));
                        if (isWallInFront()) {
                            transform.rotation = Quaternion.Euler(new Vector3(transform.rotation.x, transform.rotation.y + 180, transform.rotation.z));
                        }
                    }
                    _turnTimer = Time.time + 1;
                }
                
                state = EnemyState.Roaming;
                
                break;
            }

            case EnemyState.BeingPushed: {
                //set navmesh destination to nearest hole
                if(transform.position.y == HoleManager.Instance.floorMapping[currentFloor - 1]) {
                    state = EnemyState.Roaming;
                    currentFloor = currentFloor - 1;
                }

                break;
            }

            case EnemyState.AbleToPush: {
                if(_rand.NextDouble() < 0.5 && Time.time > _pushTimer) {
                    state = EnemyState.Pushing;
                    _pushTimer = Time.time + 3;
                }
                else {
                    state = EnemyState.Roaming;
                    _pushTimer = Time.time + 1;
                }

                break;
            }

            case EnemyState.Pushing: {
                if (!_isPushing) {
                    //set navmesh destination to target
                    _isPushing = true;
                    //send event to target 
                }
                //use navmesh to move towards destination
                //if our position equals navmesh destination
                _isPushing = false;
                state = EnemyState.Roaming;

                break;
            }

            case EnemyState.Chasing: {
                //set navmesh destination to player
                //move towards player using navmesh
                if(playerDist < playerDetectionRange && _player.GetComponent<PlayerController>().currentFloor == currentFloor + 1) {
                    state = EnemyState.SearchingAlly;
                }
                else if(playerDist < playerDetectionRange && _player.GetComponent<PlayerController>().currentFloor == currentFloor - 1) {
                    state = EnemyState.JumpingDown;   
                    //set navmesh destination to nearest hole
                }
                else if(!(playerDist < playerDetectionRange && _player.GetComponent<PlayerController>().currentFloor == currentFloor)) {
                    state = EnemyState.Roaming;
                }
                else if(transform.position == _target.transform.position) {
                    //game over
                }
                break;
            }

            case EnemyState.JumpingDown: {
                //use navmesh to move towards hole
                if(transform.position.x == nearestHole.transform.position.x && 
                transform.position.z == nearestHole.transform.position.z && 
                transform.position.y == HoleManager.Instance.floorMapping[currentFloor - 1]) {
                    state = EnemyState.Roaming;
                }
                break;
            }

            case EnemyState.SearchingAlly: {
                break;
            }

            case EnemyState.ChasingAlly: {
                break;
            }

            case EnemyState.CommsWithAllySelfInitiated: {
                break;
            }

            case EnemyState.ReturnToHoleBoosting: {
                break;
            }

            case EnemyState.Boosting: {
                break;
            }

            case EnemyState.ReturnToHoleBeingBoosted: {
                break;
            }

            case EnemyState.BeingBoosted: {
                break;
            }

            case EnemyState.CommsWithAllyOtherInitiated: {
                break;
            }

            case EnemyState.CommsWithAllyManipulatorInitiated: {
                break;
            }
            
            default: {
                state = EnemyState.Roaming;
                break;
            }
        }
    }

    private bool isWallInFront() {
        // Use raycast to check if wall is in front of us
        // Figure out how layer masks work
        // if (Physics.Raycast(transform.position, Vector3.forward, out var hit)) {
        //     if (hit.transform.gameObject.layer == "Wall") {
        //     }
        // }

        return false;
    }

    private void OnCollisionEnter(Collision other) {
        throw new NotImplementedException();
        // TODO: set isAtIntersection to true if other is of type intersection trigger volume
    }
}
