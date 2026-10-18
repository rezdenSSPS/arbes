using Unity.Netcode.Components;
using UnityEngine;

namespace AirsoftFPS
{
    // Owner-authoritative transform: each client controls their own player's
    // position and rotation directly, so movement is responsive and the host
    // sees each player where that player actually is. Server-authoritative
    // anti-cheat hardening is a later phase.
    [DisallowMultipleComponent]
    public class ClientNetworkTransform : NetworkTransform
    {
        protected override bool OnIsServerAuthoritative() => false;
    }
}
