# Abstract

BootstrapperManager is part of the Olimpo develoepr suite that contains several tools that were created to help developers. 


# How to use?
Each module that needs to be bootstrapped in the application will implement the *IBootstrapper* interface that will provide that __Startup__ and __Shutdown__ methods and the __Priority__ property. 

Executing the method __Start__ from IBootstrapperManager will start all the bootstrapped modules.

