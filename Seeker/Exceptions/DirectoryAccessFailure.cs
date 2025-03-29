using System;

namespace Seeker.Exceptions;

// TODO: Add a docstring for this exception
public class DirectoryAccessFailure(string message) : Exception(message);
