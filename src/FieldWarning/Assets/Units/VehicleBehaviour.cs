﻿/**
 * Copyright (c) 2017-present, PFW Contributors.
 *
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not use this file except in
 * compliance with the License. You may obtain a copy of the License at
 *
 * http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software distributed under the License is
 * distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See
 * the License for the specific language governing permissions and limitations under the License.
 */

using UnityEngine;

public class VehicleBehaviour : UnitBehaviour
{
    private const float DECELERATION_FACTOR = 2.5f;
    private const float ACCEL_DAMP_TIME = 0.5f;
    private const float HEADING_THRESHOLD = 3f * Mathf.Deg2Rad;

    private float _linVelocity;
    private float _rotVelocity;
    private float _forwardAccel;

    private float _terrainTiltForward, _terrainTiltRight;
    private float _terrainHeight;

    // Use this for initialization
    new void Start()
    {
        base.Start();
        Data = UnitData.Tank();
    }

    // Update is called once per frame
    new void Update()
    {
        base.Update();
    }


    protected override void DoMovement()
    {
        float targetHeading = getTargetHeading();
        float remainingTurn = CalculateRemainingTurn(targetHeading);
        float rotationSpeed = CalculateRotationSpeed(_linVelocity);
        
        float distanceToWaypoint = 0f;
        if (pathfinder.HasDestination()) {
            Vector3 waypoint = pathfinder.GetWaypoint();
            distanceToWaypoint = (waypoint - transform.localPosition).magnitude;
        }
        
        float targetSpeed = CalculateTargetSpeed(distanceToWaypoint, remainingTurn, _linVelocity, rotationSpeed);

        DoRotationalMotion(remainingTurn, rotationSpeed);
        DoLinearMotion(targetSpeed);
    }

    // Target heading currently only depends on the waypoint and final heading, but units will also need to face armor and weapons
    private float getTargetHeading()
    {
        float destinationHeading = finalHeading;

        if (pathfinder.HasDestination()) {
            var diff = pathfinder.GetWaypoint() - this.transform.position;
            if (diff.magnitude > pathfinder.finalCompletionDist)
                destinationHeading = diff.getRadianAngle();
        }

        return destinationHeading;
    }

    // Calculate the unit's maximum rotational speed in rads/sec at the given linear speed.
    // All angles need to have units of radians
    private float CalculateRotationSpeed(float linearSpeed)
    {
        float turnRadius = Mathf.Max(Data.minTurnRadius, linearSpeed * linearSpeed / Data.maxLateralAccel);

        float rotSpeed = Mathf.Deg2Rad * Data.maxRotationSpeed;
        if (turnRadius > 0f)
            rotSpeed = Mathf.Min(rotSpeed, linearSpeed / turnRadius);

        return rotSpeed;
    }
    
    private float CalculateRemainingTurn(float targetHeading)
    {
        targetHeading = targetHeading.unwrapRadian();
        //float currentHeading = Mathf.Deg2Rad * transform.localEulerAngles.y;
        return (targetHeading - rotation.y - Mathf.PI / 2).unwrapRadian();
    }

    // Finds the linear speed that gets the unit to the desired distance/angle the fastest.
    // All angles in units of radians
    private float CalculateTargetSpeed(float linDist, float remainingTurn, float linSpeed, float rotSpeed)
    {

        // Need to face approximately the right direction before speeding up
        float angDist = Mathf.Max(0f, Mathf.Abs(remainingTurn) - HEADING_THRESHOLD);
        if (angDist > Mathf.PI / 2)
            return Data.optimumTurnSpeed;

        // Want to go just fast enough to cover the linear and angular distance if the unit starts slowing down now
        float longestDist = Mathf.Max(linDist - pathfinder.finalCompletionDist/2, Data.minTurnRadius * angDist);
        float targetSpeed = Mathf.Sqrt(2 * longestDist * Data.accelRate * DECELERATION_FACTOR);

        // But not so fast that it cannot make the turn
        if (linSpeed > Data.optimumTurnSpeed && angDist > 0f)
            targetSpeed = Mathf.Min(targetSpeed, 0.4f * linDist * rotSpeed / angDist);

        return targetSpeed;
    }

    private void DoLinearMotion(float targetSpeed)
    {
        targetSpeed = Mathf.Min(targetSpeed, GetTerrainSpeed());

        if (targetSpeed > _linVelocity) {
            _forwardAccel = Data.accelRate;
        } else if (targetSpeed < _linVelocity) {
            _forwardAccel = -DECELERATION_FACTOR * Data.accelRate;
        } else {
            _forwardAccel = 0f;
        }

        if (Mathf.Abs(_forwardAccel) > 0) {
            float accelTime = (targetSpeed - _linVelocity) / _forwardAccel;
            if (accelTime < ACCEL_DAMP_TIME)
                _forwardAccel = _forwardAccel * 0.5f * (1 + accelTime/ACCEL_DAMP_TIME);

            if (_forwardAccel > 0) {
                _linVelocity = Mathf.Min(targetSpeed, _linVelocity + _forwardAccel * Time.deltaTime);
            } else {
                _linVelocity = Mathf.Max(targetSpeed, _linVelocity + _forwardAccel * Time.deltaTime);
            }
        }

        position += transform.forward * _linVelocity * Time.deltaTime;
        position.y = _terrainHeight;
    }

    private void DoRotationalMotion(float remainingTurn, float rotationSpeed)
    {
        if (Mathf.Abs(remainingTurn) < HEADING_THRESHOLD) {
            _rotVelocity = 0f;
        } else {
            _rotVelocity = Mathf.Sign(remainingTurn) * rotationSpeed;
        }
        
        var turn = _rotVelocity * Time.deltaTime;
        if (Mathf.Abs(turn) > Mathf.Abs(remainingTurn))
            turn = remainingTurn;
        rotation.y += turn;
        
        float accelTiltForward = Data.suspensionForward * _forwardAccel;
        float accelTiltRight = Data.suspensionSide * _linVelocity * _rotVelocity;

        rotation.x = _terrainTiltForward + accelTiltForward;
        rotation.z = _terrainTiltRight - accelTiltRight;
    }

    protected override Renderer[] GetRenderers()
    {
        var renderers = GetComponentsInChildren<Renderer>();
        return renderers;
    }

    public override void SetOriginalOrientation(Vector3 pos, Quaternion rotation, bool wake = true)
    {
        if (wake)
            WakeUp();
        position = pos;
        transform.position = pos;
        transform.localRotation = rotation;
        base.rotation = rotation.eulerAngles;
    }

    public override void UpdateMapOrientation()
    {
        // This way of doing the rotation should look nice because the unit won't sink into the ground
        //      much assuming length and width are set correctly, but it is not very fast

        // Apparently our forward and backward are opposite of the Unity convention
        float frontHeight = Terrain.activeTerrain.SampleHeight(transform.position + forward * Data.length/2);
        float rearHeight = Terrain.activeTerrain.SampleHeight(transform.position - forward * Data.length/2);
        float leftHeight = Terrain.activeTerrain.SampleHeight(transform.position - right * Data.width/2);
        float rightHeight = Terrain.activeTerrain.SampleHeight(transform.position + right * Data.width/2);

        _terrainHeight = Mathf.Max((frontHeight + rearHeight) / 2, (leftHeight + rightHeight) / 2);
        _terrainTiltForward = Mathf.Atan((frontHeight - rearHeight) / Data.length);
        _terrainTiltRight = Mathf.Atan((rightHeight - leftHeight) / Data.width);
    }

    protected override bool IsMoving()
    {
        return Mathf.Abs(_linVelocity) > 0f || Mathf.Abs(_rotVelocity) > 0f;
    }

    public override bool OrdersComplete()
    {
        return !pathfinder.HasDestination();
    }
}