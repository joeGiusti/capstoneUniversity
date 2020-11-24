using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;
using TMPro;

public class ControllerInput : MonoBehaviour
{
    // Public variables for easy adjustments in unity
    [Header("Basic Variables")]
    [Tooltip("Player speed will be limited to this variable")]
    public float movementSpeed = 4f;
    [Tooltip("Maximum distance player can teleport")]
    public float maxTeleportDistance = 8;
    [Tooltip("The amount of force added to player in y direction upon jump")]
    public float jumpForce = 300f;

    public GameObject toThrow;

    [Header("Advanced")]
    [Tooltip("If the XR Grab interactable is not working set this to true")]
    public bool holdUsablesInPlace, transformHold = true;
    public float physicsMovementAdjustment = 1000;
    public float physicsDampener = 0.9f;     

    [SerializeField]
    [Tooltip("0 : Continuous | 1 : Ground Teleport | 2 : Sphere Teleport")]
    public int movementType = 0;
    public int rotateIncrement = 25;
    public bool usePhysicsMovement;

    // Script variables
    InputDevice right, left;
    GameObject vrCam, menu, teleportVisual;
    Transform leftHandTransform, rightHandTransform;
    Rigidbody rigidbody, distanceGrabbingRigidbodyL, distanceGrabbingRigidbodyR, leftUsableRigidbody, rightUsableRigidbody;
    CapsuleCollider capsuleCollider;
    float spaceTeleDistance = 5f, fallingSpeed = 0f;
    LayerMask notPlayer;
    Usable leftusable, rightusable;
    XRRig xrRig;
    LineAim leftLineAim, rightLineAim;
    HolsterHolder holsters;
    public TextMeshProUGUI menuDisplay;

    // Flags
    bool justTurned = false, justPressedRightTrigger, justPressedLeftTrigger, makeWall, justTeleported, isTeleporting;
    bool justJumpedBack = false;
    bool inMenu = false;
    bool justPressedMenu = false;
    bool onGround = true, justJumped;

    // Constants
    float gravityAcceleration = -9.81f;
    
    // Get starting links
    void Start()
    {
        // Controller and XR refs
        right = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        left = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
        rightHandTransform = GetComponentInChildren<RightHand>().transform;
        leftHandTransform = GetComponentInChildren<LeftHand>().transform;
        rightLineAim = rightHandTransform.GetComponent<LineAim>();
        leftLineAim = leftHandTransform.GetComponent<LineAim>();
        xrRig = GetComponent<XRRig>();

        // Components
        rigidbody = GetComponent<Rigidbody>();
        capsuleCollider = GetComponent<CapsuleCollider>();
        vrCam = GetComponentInChildren<Camera>().gameObject;        
        menu = GetComponentInChildren<Canvas>().gameObject;
        menu.SetActive(false);
        holsters = GetComponentInChildren<HolsterHolder>();
        teleportVisual = GetComponentInChildren<TeleportVisualScript>().gameObject;

        // Binary opposite of player layer (so everything else)
        notPlayer =~ LayerMask.GetMask("Player");

        if (holdUsablesInPlace)
            InvokeRepeating("HoldUsablesInPlace", 0, (float)(1f / 120f));
    }
    void Update()
    {
        // Vr environment
        FollowRoomPosition();
        CheckIfOnGround();        

        // Controller input
        RightControllerInput();
        LeftControllerInput();

        // Hold usable
        //if(holdUsablesInPlace)
          //  HoldUsablesInPlace();
    }

    // ________________________________________________________________________________
    //                                                             Left Input Functions
    void LeftControllerInput()
    {
        LeftPrimary2D();
        LeftTrigger();
        LeftGrip();
        LeftPrimaryButton();
        LeftSecondaryButton();
        LeftMenuButton();
    }
    // The Main Left Controller Functions
    void LeftPrimary2D()
    {
        // Get the input
        left.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 left2D);

        // If there is a usable and its function overrides the default, return
        if (leftusable != null && leftusable.UseJoystick(left2D) == 0)
            return;

        // Move based on selected movement type
        if (movementType == 0)
            Move(left2D);
        else if (movementType == 1)
            SurfaceTeleport(left2D);
    }
    void LeftTrigger()
    {
        // Get the left trigger pressure
        left.TryGetFeatureValue(CommonUsages.trigger, out float triggerPressureLeft);

        // The default triggger function with a flag denoting not left (sets justPressedLeftTrigger in default)
        if (leftusable == null && triggerPressureLeft > 0.25f)        
            DefaultTriggerFunction(true, triggerPressureLeft);
        
        // Allows toggle instead of continuous button press in menu
        if (triggerPressureLeft < 0.2f)
            justPressedLeftTrigger = false;

        // If there is a usable use it, if it returns 1 also do default
        if (leftusable == null || leftusable.UseTrigger(triggerPressureLeft) == 0)
            return;

    }
    void LeftGrip() {

        // Get the left grip force
        left.TryGetFeatureValue(CommonUsages.grip, out float gripForce);

        // If not holdig a usable object and force is over the threshold: look for a usable object
        if (leftusable == null && gripForce > 0.2f)        
            LookForusable(true);
        
        // If player releases grip drop any usable object being held
        if (leftusable != null && gripForce <= 0.01)        
            Dropusable(true);

        // If grip pressure is over threshold look for objects to distance grab
        if (leftusable == null && gripForce > 0.3f)
            DistanceGrab(true, gripForce);
        else
            QuitDistanceGrab(true);

        // If grip strength is over threshold show distance grab line, else turn it off
        if (leftusable == null && gripForce > 0.05f)
            leftLineAim.uses[2] = true;
        else
            leftLineAim.uses[2] = false;
    }
    void LeftPrimaryButton() {
        
        // Get the input
        left.TryGetFeatureValue(CommonUsages.primaryButton, out bool buttonPressed);

        // If there is a usable and its function overrides the default, return
        if (leftusable != null && leftusable.UsePrimaryButton(buttonPressed) == 0)
            return;

    }
    void LeftSecondaryButton() {

        // Get the input
        left.TryGetFeatureValue(CommonUsages.secondaryButton, out bool buttonPressed);

        // If there is a usable and its function overrides the default, return
        if (leftusable != null && leftusable.UseSecondaryButton(buttonPressed) == 0)
            return;

    }
    void LeftMenuButton() {

        left.TryGetFeatureValue(CommonUsages.menuButton, out bool pressingMenu);

        // This flag makes the button a toggle
        if (!pressingMenu)
            justPressedMenu = false;

        // Toggle the menu if the button was just pushed
        if (pressingMenu && !justPressedMenu)
        {
            // Set active state to the opposite of what it is now
            menu.SetActive(!menu.activeSelf);
            justPressedMenu = true;

            // If the menu is active turn on right aim
            if (menu.activeSelf)
                rightLineAim.uses[3] = true;
            else
                rightLineAim.uses[3] = false;
        }

    }
    // Secondary left controller functions
    void Move(Vector2 left2D)
    {
        // Make a 3D movement vector from the 2D input
        Vector3 moveVector = new Vector3(left2D.x, 0, left2D.y);
        
        // Adjust it so it is in the direction the player is looking 
        moveVector = vrCam.transform.TransformDirection(moveVector);
        // (in x and z only)
        moveVector.y = 0;

        // Apply it to the rigidbody 
        //  

        // Physics movement        
        if (usePhysicsMovement)
        {
            // Apply a force to the rigidbody
            rigidbody.AddForce(moveVector * movementSpeed * Time.deltaTime * physicsMovementAdjustment);

            // Limit the velocity to player move speed
            float velocityMagnatude = rigidbody.velocity.magnitude;
            if (velocityMagnatude > movementSpeed)
                rigidbody.velocity *= movementSpeed / velocityMagnatude;

            // Quicker stopping to avoid motion sickness
            if (left2D.magnitude < 0.1f)
                // Dampen the x and z velocity, y is left the same as to not interfere with gravity
                rigidbody.velocity = new Vector3(rigidbody.velocity.x * physicsDampener, rigidbody.velocity.y, rigidbody.velocity.z * physicsDampener);

        }
        // Direct movement
        else
        {
            // Move the rigidbody
            rigidbody.MovePosition(transform.position + (moveVector * movementSpeed * Time.deltaTime));

            // Dampen the x and z movement so player is not knocked around, y is left the same as to not interfere with gravity
            rigidbody.velocity = new Vector3(rigidbody.velocity.x * physicsDampener, rigidbody.velocity.y, rigidbody.velocity.z * physicsDampener);
        }


        
        
    }
    void SurfaceTeleport(Vector2 left2D)
    {
        // Show a visual of where the player will teleport        
        if (teleportVisual != null)
        {
            if (left2D.y > 0.05f && leftLineAim.hitSomething && (leftLineAim.hit.point - transform.position).magnitude < maxTeleportDistance)
            {                                
                teleportVisual.SetActive(true);
                teleportVisual.transform.position = leftLineAim.hit.point;                
            }
            else
            {
                teleportVisual.SetActive(false);                
            }
        }
            
        // If left raycaster has a collider and y axis input is over threshold teleport to it
        if (!justTeleported && left2D.y > 0.6f && leftLineAim.hitSomething && (leftLineAim.hit.point - transform.position).magnitude < maxTeleportDistance)
        {
            // This function adjusts for world space and height
            TeleportTo(leftLineAim.hit.point);            
            justTeleported = true;

            // Sets a variable that prevents items from being dropped from the holsters
            holsters.SetWaitTime(20);
        }

        // Flag to allow one teleportation at a time
        if (left2D.y < 0.4f)
            justTeleported = false;

        // Turn on left rayCaster
        if (left2D.y > 0.05f)
            leftLineAim.uses[1] = true;
        else
            leftLineAim.uses[1] = false;

        
        // Jump back a short distance when pulling back on the joystick
        if(!justJumpedBack && left2D.y < -0.4f)
        {
            // Calculate position to jump back too
            Vector3 backOffset = vrCam.transform.TransformDirection(0, 0, -1);
            backOffset.y = -2*xrRig.cameraInRigSpaceHeight;
            backOffset = backOffset.normalized;
            
            // TeleportTo factors in the room scale
            TeleportTo(vrCam.transform.position + backOffset);

            // Flags prevent rapid movenent and holster drop 
            holsters.SetWaitTime(20);
            justJumpedBack = true;
        }

        // Reset the flag
        if(left2D.y > -0.3f)
            justJumpedBack = false;
    }
    void TeleportTo(Vector3 newPosition)
    {
        // Calculate the offset and move the player to the given position
        Vector3 colliderOffset = new Vector3(-capsuleCollider.center.x, 0, -capsuleCollider.center.z);
        colliderOffset = transform.TransformDirection(colliderOffset);
        colliderOffset.y = 0; // capsuleCollider.center.y-0.5f;
        transform.position = newPosition + colliderOffset;
    }
    // Menu functions
    public void MovementTypeUp()
    {
        movementType = (movementType + 1) % 2;
        if (movementType == 0)
            menuDisplay.SetText("Continuous");
        else
            menuDisplay.SetText("Teleport");
    }
    public void QuitGame()
    {
        // Quit the appliction
        Application.Quit();
    }

    // ________________________________________________________________________________
    //                                                            Right Input Functions
    void RightControllerInput()
    {
        RightPrimary2D();
        RightTrigger();
        RightGrip();
        RightPrimaryButton();
        RightSecondaryButton();        
    }
    // The Main Right Controller Functions
    void RightPrimary2D()
    {
        // Get the input
        right.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 right2D);

        // If there is a usable and its function overrides the default, return
        if (rightusable != null && rightusable.UseJoystick(right2D) == 0)
            return;

        // Use it to rotate the player
        Rotate(right2D);

        // Use it to fly
        if(movementType == 0)
            Fly(right2D);
    }
    void RightTrigger() {

        // Get the right trigger pressure
        right.TryGetFeatureValue(CommonUsages.trigger, out float triggerPressure);

        // If the menu is open and pressure is over threshold look for button to push
        if (menu.activeSelf && !justPressedRightTrigger && triggerPressure > 0.25f)        
            TryPushUIButton();     
        else if (!justPressedRightTrigger && rightusable == null && triggerPressure > 0.25f)        
            DefaultTriggerFunction(false, triggerPressure);


        // Allows toggle instead of continuous button press in menu
        if (triggerPressure < 0.2f)
            justPressedRightTrigger = false;

        // If there is a usable and its function overrides the default, return
        if (rightusable != null && rightusable.UseTrigger(triggerPressure) == 0)
            return;
    }
    void RightGrip() {

        // Get the grip force
        right.TryGetFeatureValue(CommonUsages.grip, out float gripForce);

        // If not holdig a usable object and force is over the threshold look for a usable object
        if (rightusable == null && gripForce > 0.2f)
            LookForusable(false);

        // If player releases grip drop any usable object being held
        if (rightusable != null && gripForce <= 0.01)
            Dropusable(false);

        // If grip pressure is over threshold look for objects to distance grab
        if (rightusable == null && gripForce > 0.3f)
            DistanceGrab(false, gripForce);
        else
            QuitDistanceGrab(false);

        // If grip strength is over threshold show distance grab line, else turn it off
        if (rightusable == null && gripForce > 0.05f)
            rightLineAim.uses[2] = true;
        else
            rightLineAim.uses[2] = false;
    }
    void RightPrimaryButton() {

        // Get the primarry button input
        right.TryGetFeatureValue(CommonUsages.primaryButton, out bool buttonPressed);

        // If there is a usable and its function overrides the default, return
        if (rightusable != null && rightusable.UsePrimaryButton(buttonPressed) == 0)
            return;

        // If the button is pressed and the player is on the ground jump
        if (!justJumped && buttonPressed && onGround)
            Jump();
    }
    void RightSecondaryButton() {
        // Get the primarry button input
        right.TryGetFeatureValue(CommonUsages.secondaryButton, out bool buttonPressed);

        // If there is a usable and its function overrides the default, return
        if (rightusable != null && rightusable.UseSecondaryButton(buttonPressed) == 0)
            return;
    }
    // Secondary right controller functions    
    void Fly(Vector2 right2D)
    {
        if ((right2D.y > 0.1f || right2D.y < -0.1f))
        {
            // The non-physics way
            //rigidbody.MovePosition(transform.position + new Vector3(0, right2D.y * movementSpeed * Time.deltaTime, 0));

            // Using physics
            rigidbody.AddForce(new Vector3(0, right2D.y * movementSpeed * 500 * Time.deltaTime, 0));
        }        
    }
    void Rotate(Vector2 right2D)
    {
        // if the joystick is near center reset the flag
        if (right2D.x < 0.2 && right2D.x > -0.2)
            justTurned = false;

        // if the joystick is outside the threshold and the flag is false: rotate and set flag
        if (right2D.x > 0.3 && justTurned == false)
        {
            xrRig.RotateAroundCameraUsingRigUp(rotateIncrement);
            justTurned = true;
        }
        if (right2D.x < -0.3 && justTurned == false)
        {
            xrRig.RotateAroundCameraUsingRigUp(-rotateIncrement);
            justTurned = true;
        }
    }
    void Jump()
    {
        // Jump by adding force to the rigidbody
        rigidbody.AddForce(Vector3.up * jumpForce);

        // Linits jump frequency
        justJumped = true;

        // At least 1/2 second pause between jumps
        Invoke("ResetJumpVariable", 0.5f);
    }    
    void ResetJumpVariable()
    {
        justJumped = false;
    }   
    void TryPushUIButton()
    {
        // If the trigger was not just pulled, and there is a line aim with a hit collider
        if (!justPressedRightTrigger &&  rightLineAim.uses[3] && rightLineAim.hitSomething)
        {
            // Look for a UI Button
            UnityEngine.UI.Button button = rightLineAim.hit.collider.GetComponent<UnityEngine.UI.Button>();
            if (button != null)
            {
                // Interact with unity UI Button
                button.OnSubmit(null);

                // Set flag for non-continuous pressing
                justPressedRightTrigger = true;
            }
        }
    }

    // ________________________________________________________________________________
    //                                                          Ambidexterous Functions
    void LookForusable(bool leftHand)
    {
        // Only look if nothing is being held in that hand
        if (leftHand && leftusable != null)
            return;
        else if (rightusable != null)
            return;

        // Get all nearby (non player) colliders
        Collider[] colliders;
        if (leftHand)
            colliders = Physics.OverlapSphere(leftHandTransform.position, 0.05f, notPlayer);
        else
            colliders = Physics.OverlapSphere(rightHandTransform.position, 0.05f, notPlayer);
        
        // Check them for usable script (Meaning they are usable objects)
        foreach (Collider c in colliders)
        {
            // Pick up usable object if there is one (short circut of && ensures no errors)
            Usable tempusable = c.gameObject.GetComponent<Usable>();
            if (tempusable != null && tempusable.beingHeld == false)
            {
                // Get a reference to its rigidbody for velocity adjustments
                Rigidbody usableRigidbody = tempusable.gameObject.GetComponent<Rigidbody>();
                usableRigidbody = c.gameObject.GetComponent<Rigidbody>();

                // Setting these variables means the usable object is being held
                if (leftHand)
                {
                    // Set variables
                    leftusable = tempusable;
                    if (leftUsableRigidbody == null)
                        leftUsableRigidbody = usableRigidbody;
                }
                else
                {
                    // Set variables
                    rightusable = tempusable;
                    if (rightUsableRigidbody == null)
                        rightUsableRigidbody = usableRigidbody;
                }

                // Set its beihg held flag
                tempusable.beingHeld = true;

                

                // usable object should not collide with the player holding it
                Collider usableCollider;
                if (leftHand)
                    usableCollider = leftusable.gameObject.GetComponent<Collider>();
                else
                    usableCollider = rightusable.gameObject.GetComponent<Collider>();
                if (usableCollider != null)
                    Physics.IgnoreCollision(usableCollider, capsuleCollider);
            }
        }
    }
    void Dropusable(bool leftHand)
    {
        Collider usableCollider;

        // Setting these variables means the usable object is no longer being held
        if (leftHand)
        {
            // The collider of the usable object will collide with the player again
            if (leftusable != null)
            {
                usableCollider = leftusable.gameObject.GetComponent<Collider>();
                Physics.IgnoreCollision(usableCollider, capsuleCollider, false);
            }
                
            // Set variables
            leftusable.beingHeld = false;
            leftusable = null;
            leftUsableRigidbody = null;
        }
        else
        {
            // The collider of the usable object will collide with the player again
            if (rightusable != null)
            {
                usableCollider = rightusable.gameObject.GetComponent<Collider>();
                Physics.IgnoreCollision(usableCollider, capsuleCollider, false);
            }

            // Set variables
            rightusable.beingHeld = false;
            rightusable = null;
            rightUsableRigidbody = null;
        }
    }
    void DistanceGrab(bool leftHand, float force)
    {
        if (leftHand)
        {
            // If there is something in distance grab usable slot pull it toward the player
            if (distanceGrabbingRigidbodyL != null)
            {
                // Calculate the vector between the usable and the player
                Vector3 usableToPlayer = leftHandTransform.position - distanceGrabbingRigidbodyL.transform.position;

                // If the object is far from the player bring it toward them
                if (usableToPlayer.magnitude > 0.5)
                {
                    // Add a force toward the player
                    distanceGrabbingRigidbodyL.AddForce(force * 40 * usableToPlayer.normalized);

                    // Limit the velocity
                    if (distanceGrabbingRigidbodyL.velocity.magnitude > 4)
                        distanceGrabbingRigidbodyL.velocity *= 4 / distanceGrabbingRigidbodyL.velocity.magnitude;
                }
                // If the object is close move it to their hand
                else
                {
                    // Set the velocity to 0
                    distanceGrabbingRigidbodyL.velocity = new Vector3();

                    // Put the object they are distance grabbing in their hand
                    distanceGrabbingRigidbodyL.gameObject.transform.position = leftHandTransform.position;
                    leftusable = distanceGrabbingRigidbodyL.gameObject.GetComponent<Usable>();
                    distanceGrabbingRigidbodyL.GetComponent<Usable>().beingHeld = true;
                }
            }
            // If there is nothing in distance grab slot and the line hit something look for a usable
            else if (leftLineAim.hitSomething)
            {
                // Look for a usable object
                Usable tempusable = leftLineAim.hit.collider.GetComponent<Usable>();

                // If one if found look for its rigidbody
                if (tempusable != null)
                    distanceGrabbingRigidbodyL = tempusable.GetComponent<Rigidbody>();
            }
        }
        else
        {
            // If there is something in distance grab usable slot pull it toward the player
            if (distanceGrabbingRigidbodyR != null)
            {
                // Calculate the vector between the usable and the player
                Vector3 usableToPlayer = rightHandTransform.position - distanceGrabbingRigidbodyR.transform.position;

                // If the object is far from the player bring it toward them
                if (usableToPlayer.magnitude > 0.5)
                {
                    // Add a force toward the player
                    distanceGrabbingRigidbodyR.AddForce(force * 40 * usableToPlayer.normalized);

                    // Limit the velocity
                    if (distanceGrabbingRigidbodyR.velocity.magnitude > 4)
                        distanceGrabbingRigidbodyR.velocity *= 4 / distanceGrabbingRigidbodyR.velocity.magnitude;
                }
                // If the object is close move it to their hand
                else
                {
                    // Set the velocity to 0
                    distanceGrabbingRigidbodyR.velocity = new Vector3();

                    // Put the object they are distance grabbing in their hand
                    distanceGrabbingRigidbodyR.gameObject.transform.position = rightHandTransform.position;
                    rightusable = distanceGrabbingRigidbodyR.gameObject.GetComponent<Usable>();
                    distanceGrabbingRigidbodyR.GetComponent<Usable>().beingHeld = true;
                }
            }
            // If there is nothing in distance grab slot and the line hit something look for a usable
            else if (rightLineAim.hitSomething)
            {
                // Look for a usable object
                Usable tempusable = rightLineAim.hit.collider.GetComponent<Usable>();

                // If one if found look for its rigidbody
                if (tempusable != null)
                    distanceGrabbingRigidbodyR = tempusable.GetComponent<Rigidbody>();
            }
        }        
    }
    // Turn off distance grab
    void QuitDistanceGrab(bool left)
    {
        if (left)
        {
            distanceGrabbingRigidbodyL = null;
            leftLineAim.uses[2] = false;
        }
        else
        {
            distanceGrabbingRigidbodyR = null;
            rightLineAim.uses[2] = false;
        }
    }
    void DefaultTriggerFunction(bool leftHand, float triggerPressure)
    {
        
        // Set variables based on parameters
        Transform handTransform;
        if (leftHand)
        {
            // Toggle flag
            if (justPressedLeftTrigger)
                return;

            // Set the transform
            handTransform = leftHandTransform;

            // Toggle flag set
            justPressedLeftTrigger = true;
        }
        else
        {
            // Toggle flag
            if (justPressedRightTrigger)
                return;

            // Set the transform
            handTransform = rightHandTransform;
            
            // Toggle flag set
            justPressedRightTrigger = true;
        }

        // Throw specified object from correct controller
        Throw(handTransform);
            
        
    }
    void Throw(Transform throwFrom)
    {
        // If there is no game object specified return
        if (toThrow == null)
            return;

        // Create the specified game object
        GameObject temp = Instantiate(toThrow);
        temp.transform.position = throwFrom.position + throwFrom.TransformDirection(new Vector3(0, 0, 0.25f));

        // Add a force to it (if it has a rigidbody)
        Rigidbody tempRigidbody = temp.GetComponent<Rigidbody>();
        if (tempRigidbody != null)
            // Mostly forward with a little upward for an arc
            tempRigidbody.AddForce(throwFrom.TransformDirection(0, 100, 600));
    }
    
    void HoldUsablesInPlace()
    {
        // If there is a left usable being held set its position, rotation, and velocity
        if (leftusable != null)
        {
            // Get a link if one is not already established
            if (leftUsableRigidbody == null)
                leftUsableRigidbody = leftusable.gameObject.GetComponent<Rigidbody>();
            
            // Rotation and velocity adjustment
            if (leftUsableRigidbody != null)
            {
                // Match rotation smoothed by rigidbody
                leftUsableRigidbody.rotation = leftHandTransform.rotation;

                // Set the velocity to 0 to prevent buildup
                leftUsableRigidbody.velocity = Vector3.zero;
            }

            // Transform hold position can be jittery on movement
            if (transformHold || rightUsableRigidbody == null)
            {
                // Use transform to match position
                leftUsableRigidbody.transform.position = leftHandTransform.position;

            }
            // Rigidbody hold lags behind on continous movement
            else
            {
                // Use rigidbody to match position
                leftUsableRigidbody.position = leftHandTransform.position;
            }


        }

        // If there is a right usable being held set its position, rotation, and velocity
        if (rightusable != null)
        {
            // Get a link if one is not already established
            if (rightUsableRigidbody == null)
                rightUsableRigidbody = rightusable.gameObject.GetComponent<Rigidbody>();
            
            // Rotation and velocity adjustment
            if (rightUsableRigidbody != null)
            {
                // Match rotation smoothed by rigidbody
                rightUsableRigidbody.rotation = rightHandTransform.rotation;

                // Set the velocity to 0 to prevent buildup
                rightUsableRigidbody.velocity = Vector3.zero;
            }

            // Transform hold position can be jittery on movement
            if (transformHold || rightUsableRigidbody == null)
            {
                // Use transform to match position
                rightusable.transform.position = rightHandTransform.position;
                //rightusable.transform.rotation = rightHandTransform.rotation;
            }
            // Rigidbody hold lags behind on movement
            else
            {
                // Use rigidbody to match position
                rightUsableRigidbody.position = rightHandTransform.position;
                //rightUsableRigidbody.rotation = rightHandTransform.rotation;
            }
        }
    }
    
    // Environment functions
    public void FollowRoomPosition()
    {

        // Adjust the character controllers center with the cam x y position in room scale
        Vector3 localCCPos = transform.InverseTransformPoint(vrCam.transform.position);
        localCCPos.y = capsuleCollider.height / 2;
        capsuleCollider.center = localCCPos;

        // Adjust the character controller height based on room scale height
        capsuleCollider.height = xrRig.cameraInRigSpaceHeight;
    }
    void CheckIfOnGround()
    {
        // Send out a spherecast to see if the player is on the ground
        if (Physics.SphereCast(transform.TransformPoint(capsuleCollider.center), capsuleCollider.radius, Vector3.down, out RaycastHit hit, (capsuleCollider.height / 2) - capsuleCollider.radius + 0.1f, notPlayer))
            onGround = true;
        else
            onGround = false;
    }

    // Debugging functions
    void DB(object o)
    {
        if(o != null)
            Debug.Log(o);
    }
    public void testFunction()
    {
        DB("test");
    }


}