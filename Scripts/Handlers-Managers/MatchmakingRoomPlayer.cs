using System;
using Mirror;

public class MatchmakingRoomPlayer : NetworkRoomPlayer
{
    public static event Action<MatchmakingRoomPlayer> AuthorityStarted;

    public event Action<bool> ReadyStateChangedClient;
    public event Action<float> MatchFound;
    public event Action MatchCountdownCancelled;

    public override void OnStartAuthority()
    {
        base.OnStartAuthority();
        AuthorityStarted?.Invoke(this);
    }

    public override void ReadyStateChanged(bool oldReadyState, bool newReadyState)
    {
        base.ReadyStateChanged(oldReadyState, newReadyState);

        ReadyStateChangedClient?.Invoke(newReadyState);

        if (isServer && NetworkManager.singleton is CustomNetworkManager customManager)
        {
            customManager.NotifyRoomPlayerStateChanged();
        }
    }

    public override void OnStopAuthority()
    {
        base.OnStopAuthority();
        ReadyStateChangedClient = null;
        MatchFound = null;
        MatchCountdownCancelled = null;
    }

    public void RequestSetReady(bool ready)
    {
        if (!isOwned)
            return;

        CmdChangeReadyState(ready);
    }

    [TargetRpc]
    public void TargetMatchFound(NetworkConnection target, float countdown)
    {
        MatchFound?.Invoke(countdown);
    }

    [TargetRpc]
    public void TargetMatchCountdownCancelled(NetworkConnection target)
    {
        MatchCountdownCancelled?.Invoke();
    }
}
