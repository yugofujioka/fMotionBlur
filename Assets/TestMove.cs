using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TestMove : MonoBehaviour {
    Vector3 origin;
    // Start is called before the first frame update
    void Start() {
#if UNITY_EDITOR
        Application.targetFrameRate = 120;
#endif
        origin = this.transform.localPosition;
    }

    // Update is called once per frame
    void Update() {
        this.transform.localPosition = origin + new Vector3(Mathf.PingPong(Time.unscaledTime * 4f, 2f), 0f, 0f);
    }
}
