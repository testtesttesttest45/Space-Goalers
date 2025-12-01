using UnityEngine;
using UnityEngine.EventSystems;

public class EventSystemSingleton : MonoBehaviour
{
    private void Awake()
    {
        EventSystem eventSystem = GetComponent<EventSystem>();
        if (EventSystem.current == null)
        {
            eventSystem.enabled = true;
        }
        else
        {
            Destroy(gameObject);
        }
    }
}
