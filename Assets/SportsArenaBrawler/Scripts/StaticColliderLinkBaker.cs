using UnityEngine;
using Quantum;

[assembly: QuantumMapBakeAssembly]
public class StaticColliderLinkBaker : MapDataBakerCallback
{
    public override void OnBeforeBake(QuantumMapData data)
    {
        // clear old links on the scene
        var oldLinks = QuantumMapDataBaker.FindLocalObjects<QPrototypeStaticColliderLink>(data.gameObject.scene);
        foreach (var link in oldLinks)
        {
            link.Prototype.StaticColliderIndex = -1;
        }
    }

    public override void OnBake(QuantumMapData data)
    {
    // go over all the static collider references (GO that has the component)
    // and, if the object also has a link component, bake the static collider index into it
    Debug.Log("OnBake StaticColliderLinkBaker");
        for (var i = 0; i < data.StaticCollider3DReferences.Count; i++)
        {
            var c = data.StaticCollider3DReferences[i];

            var link = c.GetComponent<QPrototypeStaticColliderLink>();
            if (link != null)
            {
                link.Prototype.StaticColliderIndex = i;
            }
        }
    }
}
