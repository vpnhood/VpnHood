﻿namespace VpnHood.Client.Exceptions;

public class NoStableVpnException()
    : Exception("VPN was connected, but it looked like the connection was not stable.");