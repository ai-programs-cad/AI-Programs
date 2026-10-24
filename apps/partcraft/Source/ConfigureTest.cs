using System;
class ConfigureTest{static int Main(string[] args){try{var imported=ProductEnvironment.Configure(args[0]);Console.WriteLine("CONFIGURE_OK locallyImportedInterop="+imported.Count);return 0;}catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}}
