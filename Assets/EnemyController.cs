using System;
using System.Collections.Generic;
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
    public float communicationRange;
    public int currentFloor;
    public bool isNearHole;
    public int intersectionRadius;
    public GameObject nearestDownwardHole;
    public GameObject nearestUpwardHole;

    private GameObject _player;
    private GameObject _target;
    private List<GameObject> _recentRejections;
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
        nearestDownwardHole = null;
        foreach (var hole in HoleManager.Instance.holes) {
            var holeCollider = hole.GetComponent<HoleCollider>();
            var holePos = holeCollider.transform.position;
            var holeDist = Vector3.Distance(holePos, transform.position);
            if (holeDist < shortestHoleDist && hole.GetComponent<HoleCollider>().floorNumber == currentFloor) {
                shortestHoleDist = holeDist;
                if (holeDist < holeDetectionRange) {
                    isNearHole = true;
                }
                nearestDownwardHole = hole;
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
                            var hole = allyController.nearestDownwardHole;
                            if (allyController.isNearHole && hole != null) {
                                var allyDirection = Vector3.Angle(allyPos, transform.position);
                                var holeDirection = Vector3.Angle(hole.transform.position, allyPos);
                                
                                if (Math.Abs(allyDirection - holeDirection) < 5) {
                                    canPush = true;
                                    _target = ally;
                                    break;
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
                // float comparison
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

                if (playerDist < playerDetectionRange && _player.GetComponent<PlayerController>().currentFloor == currentFloor + 1) {
                    var shortestUpHoleDist = float.MaxValue;
                    nearestUpwardHole = null;
                    foreach (var hole in HoleManager.Instance.holes) {
                        var holeCollider = hole.GetComponent<HoleCollider>();
                        var holePos = holeCollider.transform.position;
                        var holeDist = Vector3.Distance(holePos, transform.position);
                        if (holeDist < shortestUpHoleDist && hole.GetComponent<HoleCollider>().floorNumber == currentFloor + 1) {
                            shortestUpHoleDist = holeDist;
                            nearestUpwardHole = hole;
                        }
                    }
                    
                    state = EnemyState.SearchingAlly;
                } else if (playerDist < playerDetectionRange && _player.GetComponent<PlayerController>().currentFloor == currentFloor - 1) {
                    state = EnemyState.JumpingDown;   
                    //set navmesh destination to nearest hole
                } else if (!(playerDist < playerDetectionRange && _player.GetComponent<PlayerController>().currentFloor == currentFloor)) {
                    state = EnemyState.Roaming;
                } else if (transform.position == _target.transform.position) {
                    //game over
                }
                
                break;
            }

            case EnemyState.JumpingDown: {
                //use navmesh to move towards hole
                // float comparison
                if (transform.position.x == nearestDownwardHole.transform.position.x && 
                transform.position.z == nearestDownwardHole.transform.position.z && 
                transform.position.y == HoleManager.Instance.floorMapping[currentFloor - 1]) {
                    state = EnemyState.Roaming;
                }
                break;
            }

            case EnemyState.SearchingAlly: {
                var shortestDist = float.MaxValue;
                GameObject closestAlly = null;
                
                foreach (var ally in EnemyManager.Instance.enemies) {
                    if (!_recentRejections.Contains(ally)) {
                        var allyDist = Vector3.Distance(transform.position, ally.transform.position);
                        if (allyDist < shortestDist && ally.GetComponent<EnemyController>().currentFloor == currentFloor) {
                            shortestDist = allyDist;
                            closestAlly = ally;
                        }
                    }
                }

                if (closestAlly != null) {
                    _target = closestAlly;
                    state = EnemyState.Chasing;
                } else {
                    nearestUpwardHole = null;
                    state = EnemyState.Roaming;
                }
                
                break;
            }

            case EnemyState.ChasingAlly: {
                if (_isBeingPushed) {
                    state = EnemyState.BeingPushed;
                } else {
                    // Set nav mesh destination to target position
                    
                    var allyDist = Vector3.Distance(_target.transform.position, transform.position);
                    if (allyDist < communicationRange) {
                        state = EnemyState.CommsWithAllySelfInitiated;
                    }
                }

                break;
            }

            case EnemyState.CommsWithAllySelfInitiated: {
                if (boostStatus == BoostStatus.Undefined) {
                    // 0.3-0.5
                    var willingness = _rand.NextDouble() * 0.2 + 0.3;
                    // Send event with reference to ourself, reference to target, and willingness value
                    // OnCommsInitiated(this, _target, willingness);
                    boostStatus = BoostStatus.Waiting;
                } else if (allyBoostStatus == BoostStatus.Boosting) {
                    boostStatus = BoostStatus.BeingBoosted;
                    _recentRejections.Clear();
                    state = EnemyState.ReturnToHoleBeingBoosted;
                } else if (allyBoostStatus == BoostStatus.BeingBoosted) {
                    boostStatus = BoostStatus.Boosting;
                    _recentRejections.Clear();
                    state = EnemyState.ReturnToHoleBoosting;
                } else if (allyBoostStatus == BoostStatus.Rejection) {
                    boostStatus = BoostStatus.Rejection;
                    _recentRejections.Add(_target);
                    state = EnemyState.ReturnToHoleBeingBoosted;
                }
                
                break;
            }

            case EnemyState.ReturnToHoleBoosting: {
                // Set navmesh destination to nearest upward hole
                // float comparison
                if (transform.position.x == nearestUpwardHole.transform.position.x && 
                    transform.position.z == nearestUpwardHole.transform.position.z) {
                    state = EnemyState.Boosting;
                }
                
                break;
            }

            case EnemyState.Boosting: {
                if (_wasBoostSuccessful) {
                    _wasBoostSuccessful = false;
                    boostStatus = BoostStatus.Undefined;
                    allyBoostStatus = BoostStatus.Undefined;
                    state = EnemyState.Roaming;
                }
                
                break;
            }

            case EnemyState.ReturnToHoleBeingBoosted: {
                // Set navmesh destination to nearest upward hole
                // float comparison
                if (transform.position.x == nearestUpwardHole.transform.position.x && 
                    transform.position.z == nearestUpwardHole.transform.position.z) {
                    state = EnemyState.BeingBoosted;
                }
                
                break;
            }

            case EnemyState.BeingBoosted: {
                // if using physical holes, move to side so it won't fall back down immediately
                transform.position = new Vector3(transform.position.x, HoleManager.Instance.floorMapping[currentFloor + 1], transform.position.z);
                currentFloor++;
                // send event to ally on boost successs
                //OnBoostSuccess(this, _target);
                state = EnemyState.Roaming;
                
                break;
            }

            case EnemyState.CommsWithAllyOtherInitiated: {
                // 0.3-0.5
                var willingness = _rand.NextDouble() * 0.2 + 0.3;
                if (willingness > _rand.NextDouble()) {
                    boostStatus = BoostStatus.Boosting;
                } else if (_allyWillingnessToBoost > _rand.NextDouble()) {
                    boostStatus = BoostStatus.BeingBoosted;
                } else {
                    boostStatus = BoostStatus.Rejection;
                }
                
                // send event with our boost status
                //OnCommsResponse(this, _target, boostStatus);

                switch (boostStatus) {
                    case BoostStatus.Boosting: {
                        state = EnemyState.ReturnToHoleBoosting;
                        
                        break;
                    }
                    
                    case BoostStatus.BeingBoosted: {
                        state = EnemyState.ReturnToHoleBeingBoosted;
                        
                        break;
                    }
                    
                    default: {
                        boostStatus = BoostStatus.Undefined;
                        state = EnemyState.Roaming;
                        
                        break;
                    }
                }
                
                break;
            }

            case EnemyState.CommsWithAllyManipulatorInitiated: {
                // 0.3-0.5
                var willingness = _rand.NextDouble() * 0.2 + 0.3;
                boostStatus = willingness > _rand.NextDouble() ? BoostStatus.Boosting : BoostStatus.Rejection;
                
                // send event with our boost status
                //OnCommsResponse(this, _target, boostStatus);

                if (boostStatus == BoostStatus.Boosting) {
                    state = EnemyState.ReturnToHoleBoosting;
                } else {
                    boostStatus = BoostStatus.Undefined;
                    state = EnemyState.Roaming;
                }
                
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
