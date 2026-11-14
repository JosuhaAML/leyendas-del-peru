using Unity.Netcode.Components;

// NetworkTransform donde manda el dueño del objeto (el jugador mueve su propio personaje).
public class OwnerNetworkTransform : NetworkTransform
{
    protected override bool OnIsServerAuthoritative() => false;
}
