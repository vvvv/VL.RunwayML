# VL.RunwayML
Use [RunwayML](http://runwayml.com) models as nodes in VL.

Try it with vvvv, the visual live-programming environment for .NET  
Download: http://visualprogramming.net

## Using the library
Requires:
- vvvv gamma 2020.2.0 or later.
- A login to RunwayML and access to the [ML Lab](https://app.runwayml.com/models).

In order to use this library with VL you have to install the nuget that is available via nuget.org. For information on how to use nugets with VL, see [Managing Nugets]([https://thegraybook.vvvv.org/reference/libraries/dependencies.html#manage-nugets](https://thegraybook.vvvv.org/reference/hde/managing-nugets.html)) in the VL documentation. As described there you go to the commandline and then type:

    nuget install VL.RunwayML -pre

Once the VL.RunwayML nuget is installed and referenced in your VL document you'll see the category "RunwayML" under "ML" in the nodebrowser. 

Next to your .vl document put two files:

    \runway\hosted-models.txt
    \runway\local-models.txt

In `hosted-models.txt` you can define multiple [hosted models](https://help.runwayml.com/hc/en-us/articles/4402110855571-Hosted-Models). one per line (model name, url, token), like: 

    runway/DenseDepth, https://densedepth-636803eb.hosted-models.runwayml.cloud/v1/, 000000000000==

In `local-models.txt` you can define multiple models that you run via the locally installed RunwayML application. on per line (model name, url), like: 

    PoseNet, http://localhost:8000

Both local and hosted models will then be available as nodes in the `ML.RunwayML` category.

Demos are available via the Help Browser!
