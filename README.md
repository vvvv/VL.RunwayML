# VL.RunwayML
Use [RunwayML](http://runwayml.com) [hosted models](https://learn.runwayml.com/#/how-to/hosted-models) as nodes in VL.

Try it with vvvv, the visual live-programming environment for .NET  
Download: http://visualprogramming.net

## Using the library
In your user vvvv gamma documents folder put a file named runway.txt:

    Documents\vvvv\gamma\runway.txt
	
In the file you define one of your hosted models per line (node-name, url, token), like: 

    Text2Image, https://tti.hosted-models.runwayml.cloud/v1/, 000000000000==
    
Those will then be available as nodes in the `ML.RunwayML` category.
