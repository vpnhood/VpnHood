﻿using System;

namespace VpnHood.AccessServer.Exceptions;

public class AlreadyExistsException : Exception
{
    public AlreadyExistsException(string collectionName) :
        base($"Object already exists in {collectionName}!")
    {
        CollectionName = collectionName;
    }

    public string CollectionName { get; }
}