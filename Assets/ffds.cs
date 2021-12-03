using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ffds : MonoBehaviour
{
    // Start is called before the first frame update
    public Transform t; 
    void Start()
    {
        Vector3 deltaPos = (transform.position - t.position);
        Debug.Log("beaker pos  "+t.position);
        Debug.Log("my pos  "+transform.position);
        Debug.Log("delta pos  "+  deltaPos);

        Quaternion rot = t.parent.rotation;
        rot.eulerAngles = (new Vector3(45,45,0));
        t.parent.rotation = rot;
        
        transform.position = t.position + deltaPos;
        transform.rotation *= t.rotation;
        
        Debug.Log("beaker pos  "+t.position);
        Debug.Log("my pos  "+transform.position);
        Debug.Log("delta pos  "+ (transform.position - t.position) );
    }   

    // Update is called once per frame
    void Update()
    {
        //Debug.Log(transform.position-t.position);
    }
}
