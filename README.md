# VL.RunwayML
Use [RunwayML](http://runwayml.com) models as nodes in VL.

Try it with vvvv, the visual live-programming environment for .NET  
Download: http://visualprogramming.net

## Using the library
For now requires a vvvv gamma 2020.2-preview build which you can get [from here](https://teamcity.vvvv.org/). Login as guest.

In order to use this library with VL you have to install the nuget that is available via nuget.org. For information on how to use nugets with VL, see [Managing Nugets](https://thegraybook.vvvv.org/reference/libraries/dependencies.html#manage-nugets) in the VL documentation. As described there you go to the commandline and then type:

    nuget install VL.RunwayML -pre

Once the VL.RunwayML nuget is installed and referenced in your VL document you'll see the category "RunwayML" under "ML" in the nodebrowser. 

In your user vvvv gamma documents folder put two files:

    Documents\vvvv\gamma\runway-hosted.txt
    Documents\vvvv\gamma\runway-local.txt
	
	
In `runway-hosted.txt` you can define multiple [hosted models](https://learn.runwayml.com/#/how-to/hosted-models). on per line (model name, url, token), like: 

    runway/DenseDepth, https://densedepth-636803eb.hosted-models.runwayml.cloud/v1/, 000000000000==

In `runway-local.txt` you can define multiple models that you run via the locally installed RunwayML application. on per line (model name, url), like: 

    PoseNet, http://localhost:8000

Both local and hosted models will then be available as nodes in the `ML.RunwayML` category.

Demos are available via the Help Browser!
