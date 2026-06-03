using System;

namespace FSDB.Exceptions;

public class FileNameGenerationException(string message, Exception? inner = null) : Exception(message, inner);