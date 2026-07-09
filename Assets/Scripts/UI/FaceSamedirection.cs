using UnityEngine;

public class FaceSamedirection: MonoBehaviour
{
    // •¶š‚ªí‚ÉƒJƒƒ‰‚ğŒü‚­‚æ‚¤‚ÉŒÅ’è
    private Transform _cameraTransform;

    void   Start()
    {
        _cameraTransform = Camera.main.transform;
    }
     void Update()
    {
        transform.LookAt(transform.position + _cameraTransform.forward);
    }
}
