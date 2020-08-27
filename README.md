# VL.RunwayML
Use [RunwayML](http://runwayml.com) [hosted models](https://learn.runwayml.com/#/how-to/hosted-models) as nodes in VL.

Try it with vvvv, the visual live-programming environment for .NET  
Download: http://visualprogramming.net

## Using the library
For now requires a vvvv gamma 2020.2-preview build which you can get [from here](https://teamcity.vvvv.org/). Login as guest.

In order to use this library with VL you have to install the nuget that is available via nuget.org. For information on how to use nugets with VL, see [Managing Nugets](https://thegraybook.vvvv.org/reference/libraries/dependencies.html#manage-nugets) in the VL documentation. As described there you go to the commandline and then type:

    nuget install VL.RunwayML -pre

Once the VL.RunwayMLt nuget is installed and referenced in your VL document you'll see the category "RunwayML" under "ML" in the nodebrowser. 

In your user vvvv gamma documents folder put a file named `runway.txt`:

    Documents\vvvv\gamma\runway.txt
	
In the file you define one of your hosted models per line (model name, url, token), like: 

    runway/DenseDepth, https://densedepth-636803eb.hosted-models.runwayml.cloud/v1/, 000000000000==
    
Those will then be available as nodes in the `ML.RunwayML` category.

Demos are available via the Help Browser!
