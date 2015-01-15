NetDist
=======

NetDist (short for ".Net Distribution") is a task or job distribution system for .Net and C#. Here are some of the features it supports:
* Runtime addition / updating of packages (eg. server logic)
* Runtime adition / updating of tasks with just C# files! (eg. client logic)
* Add as many clients as you want
* All-in-one solution (server + server administrator + client)
* Easily extensible (like other communications, currently only Web Api is implemented)

### Build Status
|Repo|Appveyor|
|:---|:------------------|
|[NetDist](https://github.com/Roemer/NetDist)|[![Build status](https://ci.appveyor.com/api/projects/status/pgy2svo2oaoqf1td?svg=true)](https://ci.appveyor.com/project/RomanBaeriswyl/netdist)|

### Description
##### Packages
One of the main articacts are packages. Packages contain the server logic (handlers) (eg. how jobs are generated, the input/output file definitions, what is done with the result). They also contain any 3rd party dependencies a client might need.

##### Handlers
Handlers are dynamically instantiated on the server for each job which is loaded for this handler.

##### Job Scripts
Job scripts are simple C# files with some meta information (like the package and handler to use, libraries needed to compile or settings for the server). When uploading a job script to the server, the server creates a new instance for the wanted handler. So the same job script can be uploaded multiple times with for example different settings.

##### Server
The server is the application which loads the handlers and runs them.

##### Server Admin
The server admin is the application which allows administration of the server. It shows the status about all handlers and the clients connected to the server. It is needed to upload packages or job scripts.

##### Client
The client requests jobs from the server and completes them, sending the result back to the server.

##### Communication
Currently, all the communication is implemented in Web Api. Other channels might be implemented later.
