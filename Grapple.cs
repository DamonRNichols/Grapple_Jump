using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Grapple : MonoBehaviour {

	//Player reference
	GameObject player;
	Rigidbody playerRigid;

	//controller input
		//these allow for more that one grapple to exist in a scene with their own controller mapped
	public string fireButton;
	public string inputAxisX;
	public string inputAxisY;

	//Grapple reference
	GameObject grappledObject; //hook or create, wither is referenced here
	Vector3 grapplePointPos; //the position of the currently grappled object
	//hooks reference
		//we can swing from these
	List<GameObject> hooksInRange;
	bool hookFound;
	//crates reference
		//we can throw these with the grapple
	List<GameObject> cratesInRange;
	bool crateFound;
	Vector3 throwDir; //this is the same as the joystick direction unless we are pointing into the ground

	//grapple variables
	float targetDegreeRange = 35.0f; //how wide our range is to grapple something without missing
	float collisionRadius = 10.0f; //how far out we can grapple
	Vector3 joystickDirection;
	bool isGrappled;
	bool isGrappleReleased;

	//tether variables
	float currLength; //the distance we are at now
	float desiredLength; //Hook: the distance from the hook that we are currently trying to reach - constantly adjusted
	float givenLength; //Hook: the extra slack the slowly works towards desiredLength - 'springiness'
	float minLength, maxLength; //Hook: the limits of the rope while swinging
	float tensionMagnitude; //Crate: the amount of force to impart on the velocity when released
	float tetherSlackAvailable; //Crate: the extra tether available after grapple - impacts tensionMagnetude
	float throwForceMax = 20.0f; //Crate

	//tether renderer
	LineRenderer ropeImage;
		//for crate throwing
	Vector3[] tetherCurveJoints;
	Vector3[] tetherPosPoints;

	//aim rendering
	GameObject[] aimSprites;
	public Color aimColor; //so we can colour each grapple aiming tool differently
	int pointNum = 10;
	public Sprite aimDotSprite;
	GameObject targetSprite; //for crate throw magnitude


	// Use this for initialization
	void Start () {
		player = transform.parent.gameObject;
		playerRigid = player.GetComponent<Rigidbody>();
		GetComponent<SphereCollider> ().radius = collisionRadius;

		//Hook location
		hooksInRange = new List<GameObject>();
		hookFound = false;
		cratesInRange = new List<GameObject> ();
		crateFound = false;
		isGrappled = false;
		isGrappleReleased = true;

		//Grapple limits
		minLength = 1.6f;
		maxLength = collisionRadius;

		//Rope visualisation setup
		ropeImage = GetComponent<LineRenderer> ();
		ropeImage.startWidth = ropeImage.endWidth = 0.1f;
		ropeImage.startColor = ropeImage.endColor = Color.white;
		ropeImage.material.color = Color.white;
		ropeImage.positionCount = 2;
		ropeImage.enabled = false;
		tetherPosPoints = new Vector3[3];

		//aiming sprites
		CreateSpriteArray ();
		SpritesActive (false);
	}

	// Update is called once per frame
	void Update () {
		if (Input.GetButtonDown ("Quit")) {
			Application.Quit();
		}

	}

	void FixedUpdate () {
		targetSprite.SetActive (false);
		RenderAim (player.transform.position, GetJoystickDirection ().normalized);
		ControlGrapple ();
	}


	#region Collisions
	void OnTriggerEnter(Collider col) {
		if (col.gameObject.tag == "Hook") {
			hooksInRange.Add(col.gameObject);

			//debugging / visualisation
			col.gameObject.GetComponent<Renderer>().material.color = Color.blue;
		}

		if (col.gameObject.tag == "Crate") {
			cratesInRange.Add(col.gameObject);

			//debugging / visualisation
			col.gameObject.GetComponent<Renderer>().material.color = Color.blue;
		}
	}

	void OnTriggerExit(Collider col) {
		if (col.gameObject.tag == "Hook") {
			for (int i = 0; i < hooksInRange.Count; ++i) {
				if (col.gameObject == hooksInRange[i]) {
					//debugging / visualisation
					col.gameObject.GetComponent<Renderer>().material.color = Color.white;

					hooksInRange.RemoveAt(i);
					--i;
				}
			}
		}

		if (col.gameObject.tag == "Crate") {
			for (int i = 0; i < cratesInRange.Count; ++i) {
				if (col.gameObject == cratesInRange[i]) {
					//debugging / visualisation
					col.gameObject.GetComponent<Renderer>().material.color = Color.white;

					cratesInRange.RemoveAt(i);
					--i;
				}
			}
		}
	}
	#endregion

	#region Grapple Functions
	void ControlGrapple () {
		//check to see if the trigger is just pulled, pulled already or released
		//check that the trigger is down
		if (Input.GetAxisRaw (fireButton) > 0.2f) {
			//have we crappled anything yet?
			if (!isGrappled) {
				FindGrabbableObject (); //check theobjects nearby to see if we caught any
				if (hookFound == true) {
					grapplePointPos = grappledObject.transform.position; //get the grapple position
					isGrappled = true;
					isGrappleReleased = false;
					//start our desired length at the players distance
					desiredLength = (grapplePointPos - transform.position).magnitude;
					//turn on the rope renderer
					ropeImage.enabled = true;
				} else if (crateFound == true) {
					grapplePointPos = grappledObject.transform.position; //get the grapple position
					isGrappled = true;
					isGrappleReleased = false;

				}
			}
			if (isGrappled) {
				if (hookFound) {
					//get swinging stuff done
					RopeControl();
					//SpritesActive (false);
				} else if (crateFound) {
					//check if still in range
					if (CrateStillInRange ()) {
						//Get throwing stuff done
						PrepareThrow ();
					} else {
						isGrappleReleased = true;
					}
				}
			}
		} else { //if the trigger is off
			isGrappleReleased = true;
		}

		//where we grappled last frame?
		if (isGrappled && isGrappleReleased) {
			if (hookFound) {
				ropeImage.enabled = false;
			} else if (crateFound) {
				ThrowObject ();
			}

			isGrappled = false;

			hookFound = false;
			crateFound = false;
			grappledObject = null;
		}
	}

	public void FindGrabbableObject() {
		//check if there are any hooks or crates that we can grab onto AND that we are leaning the joystick enough
		if ((hooksInRange.Count > 0 || cratesInRange.Count > 0) &&
			Mathf.Abs (Input.GetAxis (inputAxisX)) + Mathf.Abs (Input.GetAxis (inputAxisY)) > 0.2f) {
			//get the direction fo the joystick
			joystickDirection = GetJoystickDirection ();

			float bestGrappleTheta = 181.0f; //this value is greater than the maximum possible theta value

			//loop through the nearest hooks and crates and find the one closest to the target
			if (hooksInRange.Count > 0) {
				CheckGrappleListThetas (hooksInRange, ref bestGrappleTheta);
			}
			if (cratesInRange.Count > 0) {
				CheckGrappleListThetas (cratesInRange, ref bestGrappleTheta);
			}

			//if the the grapple was in range then we hold on
			if (bestGrappleTheta < targetDegreeRange) {
				if (grappledObject.tag == "Hook") {
					hookFound = true;
					crateFound = false;
				} else if (grappledObject.tag == "Crate") {
					hookFound = false;
					crateFound = true;
				} else {
					Debug.LogError ("grappledObject has no 'Hook' or 'Crate' tag. FIX NOW");
				}
			} else {
				hookFound = false;
				crateFound = false;
				grappledObject = null; //erase any hook in the reference
			}
		} else {
			//just encase, reset everything
			hookFound = false;
			crateFound = false;
			grappledObject = null; //erase any hook in the reference
		}

	}

	void CheckGrappleListThetas (List<GameObject> list, ref float bestTheta) {
		for (int i = 0; i < list.Count; ++i) {
			//get the diff of the joystick direction and the current hook we are looking at
			float diff = GetAbsDegDiff(GetObjectDir(list[i]), joystickDirection);

			//compare the numbers and see if they are within exceptable range
			if (diff < bestTheta && diff < targetDegreeRange)
			{
				bestTheta = diff;
				grappledObject = list[i];
			}
		}
	}
	#endregion


	#region Swing and Throw functions
	void RopeControl() {
		grapplePointPos = grappledObject.transform.position; //get the grapple position ENCASE IT HAS MOVED

		//is the rope going through a platform?
			//this may or may not be used but is here just in case
		Vector3 pos = new Vector3(transform.position.x, transform.position.y);
		Vector3 direction = new Vector3(grapplePointPos.x, grapplePointPos.y) - pos;
		if (Physics.Raycast(pos, direction.normalized, direction.magnitude)) {
			isGrappleReleased = true; //cut the rope!
		}

		if (!isGrappleReleased) {
			//are we adjusting the length of the rope?
			if (Mathf.Abs(Input.GetAxis (inputAxisX)) + Mathf.Abs(Input.GetAxis (inputAxisY)) > 0.2f) {
				float diff = GetAbsDegDiff(GetObjectDir(grappledObject), GetJoystickDirection());
				//what percent are we pointing towards the hook by
				float percentDiff = 100 - (diff / 90 * 100);
				if (percentDiff != 0) { //not pointing perpendicular
					float dir = 2 * (percentDiff / 100); //direction multiplied by the magnitude

					desiredLength += -dir * 0.02f;

					//keep the desired length within the limits
					if (desiredLength < minLength) {
						desiredLength = minLength;
					} else if (desiredLength > maxLength) {
						desiredLength = maxLength;
					}
				}
			}

			//is the player trying to move while on the rope?
			if (Mathf.Abs(Input.GetAxis (inputAxisX)) > 0.1f)
			{	
				float dir;
				dir = (Input.GetAxis(inputAxisX) > 0) ? 10 : -10;
				playerRigid.velocity += new Vector3(dir, 0) * 0.02f;
			}

			//swing the player around!
			SwingPlayer ();

			//draw the rope
			ropeImage.positionCount = 2;
			ropeImage.SetPosition (0, transform.position);
			ropeImage.SetPosition (1, grapplePointPos);
		}
	}

	void SwingPlayer() {
		//calculate the current length of the rope
		currLength = (grapplePointPos - transform.position).magnitude;

		//calculate how much extra slack we want to give the rope based
		//on how far we are from the desired length
		float adjustSpeed = 0.9945f; //Hard to find balance here

		if (currLength > desiredLength + 0.2f && currLength < desiredLength + 1) {
			givenLength = currLength * adjustSpeed;
		} else if (currLength > desiredLength + 2) {
			givenLength = currLength * 0.99f;
		} else {
			givenLength = desiredLength;
		}

		//predict (roughly) the position of the player on the next frame
		Vector3 predictedPos = transform.position + new Vector3(playerRigid.velocity.x, playerRigid.velocity.y) * 0.02f;

		//if the predicted position is out the length of the tether
		if ((predictedPos - grapplePointPos).magnitude > givenLength) {
			//if the player is moving fast enough away from the hook, add some bounce
			if (playerRigid.velocity.magnitude > 6.0f)
			{
				//get the degree diff from the player direction and the grappled hook
				float diff = GetAbsDegDiff(GetObjectDir(grappledObject), playerRigid.velocity);
				//if it's going mostly in the opposite direction...
				if (diff > 135.0f)
				{
					//extend the given length by a little bit
					givenLength = currLength + ((predictedPos - grapplePointPos).magnitude - currLength) / 5;
				}
			}

			//then pull that position back towards the hook
			predictedPos = grapplePointPos + ((predictedPos - grapplePointPos).normalized * givenLength);

			//create the new velocity based on that new position
			playerRigid.velocity = (predictedPos - transform.position) / 0.02f;
		}
	}

	void PrepareThrow() {
		grapplePointPos = grappledObject.transform.position; //get the grapple position encase it has moved

		//we need to prepare the magnitude of the direction to throw it
		//find the direction it is aimed compared to player position
		joystickDirection = GetJoystickDirection ();
		float diff = GetAbsDegDiff(GetObjectDir(grappledObject),joystickDirection);
		float diffPercentage = diff / 180;

		//maximum force is thrown at half the max tether length (half sized bubble) and the more the player points away from themselves, the less power available.
		currLength = (grapplePointPos - transform.position).magnitude;
		//how much space between the grapple and max length?
		tetherSlackAvailable = ((currLength / collisionRadius) > 0.5f ? (1 - (currLength / collisionRadius)) * 2 : 1);
		//will the aimed direction impact the amount of force imparted? the more pointed away from the player, the more it lerps towards being weaker
		float dirWeakening = Mathf.Lerp(1, tetherSlackAvailable, 1-diffPercentage);

		tensionMagnitude = throwForceMax * dirWeakening;
		throwDir = joystickDirection;

		//debuging render crap
		RenderAim (grappledObject.transform.position, throwDir);
		targetSprite.transform.position = grappledObject.transform.position + ((Vector3)throwDir * tensionMagnitude);
		targetSprite.SetActive (true);

		//compare to ground normal
		AimedAgainstNormalAjustment();

		tetherPosPoints [0] = player.transform.position;
		tetherPosPoints [2] = grapplePointPos;
		tetherPosPoints [1] = Vector3.Lerp (tetherPosPoints [0], tetherPosPoints [2], 0.5f) + ((Vector3)throwDir * tensionMagnitude);
		ropeImage.enabled = true;
		tetherCurveJoints = SmoothCurve (tetherPosPoints);
		ropeImage.positionCount = tetherCurveJoints.Length;
		for (int i = 0; i < tetherCurveJoints.Length; ++i) {
			ropeImage.SetPosition (i, tetherCurveJoints [i]);
		}
	}

	void AimedAgainstNormalAjustment() {
		if (grappledObject.GetComponent<CrateCollisionScript> () != null) {
			if (grappledObject.GetComponent<CrateCollisionScript> ().isColliding) {
				//we need to nullify any force thrown towards the wall or floor the crate is resting on
				//Check if we are aiming into the surface that the crate rests on
				var normalDir = grappledObject.GetComponent<CrateCollisionScript> ().surfaceNormal;

				if (GetAbsDegDiff (normalDir, joystickDirection) > 90) {
					//get the normal's perpendicular direction
					Vector3 normalPerpDir = new Vector3 (normalDir.y, -normalDir.x, 0);
					//what's the angle difference between where we are aiming and the normal?
					float degreeDiff = GetDegDiff (normalDir, joystickDirection);
					//generate the Cos value we will use to project the tension onto the Normal Perpendicular
					float cos = Mathf.Cos (Mathf.Deg2Rad * degreeDiff);
					//generate the Dot Product
					//float dotMagnitude = ((normalPerpDir.x * joystickDirection.x * tensionMagnitude) +(normalPerpDir.y * joystickDirection.x * tensionMagnitude)) * cos;
					float dotMagnitude = (normalPerpDir).magnitude * (joystickDirection).magnitude * cos;

					Vector3 grapPoint = new Vector3 (grapplePointPos.x, grapplePointPos.y, 0);
					//get the point along the normal perp that the new aim and magnitude should be based off
					//The cos variable and degreeDiff conflict in scale as one get larger and the other gets smaller. 
					//To fight this, we lerp between them based on the (inverted) cos progression
					tensionMagnitude = (degreeDiff < 0) ? tensionMagnitude : -tensionMagnitude;
					//^^^We also may need flip the tensionMagnitude so that the newAimedpos will end up on 
					//the correct side of the normal's perpendicular line, it will be changed soon anyway.
					Vector3 newAimedPos = grapPoint + (-normalPerpDir * (Mathf.Lerp (dotMagnitude, tensionMagnitude, 1 - Mathf.Abs (cos))));

					targetSprite.transform.position = (Vector3)newAimedPos;

					throwDir = (newAimedPos - grapPoint).normalized;
					tensionMagnitude = (newAimedPos - grapPoint).magnitude / 2;
				}
			}
		} else {
			Debug.LogError ("Throwable object does not have a Collision Script attached. It is not averageing its normals");
		}
	}

	void ThrowObject() {
		grappledObject.GetComponent<Rigidbody> ().velocity += throwDir * tensionMagnitude;
		ropeImage.enabled = false;
	}

	bool CrateStillInRange() {
		bool stillInRange = false;
		foreach (GameObject crate in cratesInRange) {
			if (grappledObject == crate) {
				stillInRange = true;
			}
		}
		return stillInRange;
	}
	#endregion

	#region Target Rendering
	void RenderAim(Vector3 originPoint, Vector3 dir) {
		//if not hooked but is aiming
		if (Mathf.Abs (Input.GetAxis (inputAxisX)) + Mathf.Abs (Input.GetAxis (inputAxisY)) > 0.2f) {
			for (int i = 0; i < pointNum; ++i) {
				aimSprites [i].transform.position = originPoint + ((dir * collisionRadius) * ((i + 1) / (float)pointNum));
			}
			SpritesActive (true);
		} else {
			SpritesActive (false);
		}
	}

	void CreateSpriteArray() {
		aimSprites = new GameObject[pointNum];
		for (int i = 0; i < pointNum; ++i) {
			aimSprites[i] = new GameObject();
			aimSprites[i].transform.SetParent(GetComponentInParent<Transform>());
			SpriteRenderer renderer = aimSprites[i].AddComponent<SpriteRenderer>();
			renderer.sprite = aimDotSprite;
			renderer.color = new Color(aimColor.r,aimColor.g,aimColor.b, (pointNum-i) / (float)pointNum);
			aimSprites[i].transform.localScale = new Vector3(1,1,1);
		}

		targetSprite = new GameObject ();
		targetSprite.transform.SetParent(GetComponentInParent<Transform>());
		SpriteRenderer rend = targetSprite.AddComponent<SpriteRenderer>();
		rend.sprite = aimDotSprite;
		rend.color = new Color(aimColor.r,aimColor.g,aimColor.b, 0.8f);
		targetSprite.transform.localScale = new Vector3(3,3,3);
	}

	void SpritesActive(bool setActive) {
		if (aimSprites[0].activeInHierarchy != setActive) {
			for (int i = 0; i < pointNum; ++i) {
				aimSprites[i].SetActive(setActive);
			}
			targetSprite.SetActive(setActive);
		}
	}
	#endregion

	#region Math Functions
	Vector3 GetJoystickDirection() {
		Vector3 dir = new Vector3 (Input.GetAxis (inputAxisX), Input.GetAxis (inputAxisY), 0);
		return dir;
	}
	Vector3 GetObjectDir (GameObject obj){
		Vector3 dir = new Vector3 (obj.transform.position.x, obj.transform.position.y, 0) - new Vector3 (transform.position.x, transform.position.y, 0);
		return dir;
	}

	//Degree comparision function
	float GetDegDiff(Vector3 dir1, Vector3 dir2) {
		//turn out directions into degree, up is 0 by default
		float Theta1 = Mathf.Atan2(dir1.x, dir1.y) * Mathf.Rad2Deg;
		float Theta2 = Mathf.Atan2(dir2.x, dir2.y) * Mathf.Rad2Deg;

		//get the difference between the two
		float diff = -Theta1 + Theta2;

		//it may turn out to go beyond negative 180 or positive 180
		//so add or subtract 360 to put it back in the correct range
		if (diff > 180.0f) {
			diff -= 360.0f;
		} else if (diff < -180.0f) {
			diff += 360.0f;
		}
		return diff;
	}

	float GetAbsDegDiff(Vector3 dir1, Vector3 dir2) {
		//same as the previous function but we only care to check and return absolute values
		float Theta1 = Mathf.Atan2(dir1.x, dir1.y) * Mathf.Rad2Deg;
		float Theta2 = Mathf.Atan2(dir2.x, dir2.y) * Mathf.Rad2Deg;

		float diff = -Theta1 + Theta2;
		diff = (Mathf.Abs(diff) > 180.0f) ? Mathf.Abs(diff - 360.0f) : Mathf.Abs(diff);
		return diff;
	}

	//curving tether when holding a crate
	Vector3[] SmoothCurve(Vector3[] tetherToCurve) {
		Vector3[] curveJoints; //a small array to joints to put in between

		int tetherLength = tetherToCurve.Length;

		//smoothness multiplies the amount of joints in the array to curve it nicer
		int smoothness = 5; //might want play around with this

		int curvedLength = (tetherLength * smoothness) -1;
		List<Vector3> curveList = new List<Vector3>(curvedLength);

		float t = 0.0f; //lerp tracker
		//loop through the curve points
		for (int i = 0; i < curvedLength + 1; i++) {
			//move t to the right point
			t = Mathf.InverseLerp (0, curvedLength, i);

			curveJoints = tetherToCurve; //copy tether joints

			//loop backwards and move joints
			for (int j = tetherLength - 1; j > 0; j--) {
				//shift a joint based on the one infront and behind it
				for (int k = 0; k < j; k++) {
					curveJoints [k] = (1 - t) * curveJoints [k] + t * curveJoints [k + 1];
				}
			}

			curveList.Add (curveJoints [0]);
		}

		return (curveList.ToArray ());
	}
	#endregion
}
