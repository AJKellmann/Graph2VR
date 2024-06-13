# Graph2VR

Graph2VR is a PhD project, a prototype for a VR application to visualize graphs (SPARQL) as 3D graphs in Virtual Reality. The idea is to explore, analyze, and interact with the data in the graph using gesture control. Graph2VR has been built in Unity and is able to connect to a SPARQL endpoint using [dotNetRDF](https://dotnetrdf.org/). We got inspired by many different tools to work with Linked data and to visualize Graphs. Virtual Reality offers the user way more space to expand the graph than a 2D computer screen.

## Documentation

- For detailed instructions on how to use Graph2VR, refer to the [Graph2VR User Manual](https://doi.org/10.5281/zenodo.8040594).
- Our research paper on Graph2VR is available [here](https://doi.org/10.1093/database/baae008).<br>
  The paper compares similar tools, provides insights into the design process of Graph2VR and contains the results from our usability study.

## Features

Here is an example of a query that shows the results in form of stacked 2D graphs behind each other:

<img src="https://github.com/molgenis/Graph2VR/assets/49238704/aa144a7e-96c6-474b-b8b4-a807d1b3e6b1" width="400">

We tried to write a GUI to explore and interact with the graph in Virtual Reality. 
A few simple operations to do so are:

- Getting information about the current node
- Expanding the graph at a node using incoming or outgoing predicates
- Deleting a node or collapsing unconnected nodes
- Comparing different parts of the graph side by side
- Building visual query patterns for SPARQL queries
- Adding new nodes and edges
- Interacting with the graph (zoom, move, rotate)
- Using visual queries or a search function to add specific nodes
- Saving results in ntriple format for reuse in other programs

<img src="https://github.com/molgenis/Graph2VR/assets/49238704/45a87902-f7f3-43d7-8e38-d05b2a12bb35" width="400">

Different Layout Algorithms can help to visualize the data.

<img src="https://github.com/molgenis/Graph2VR/assets/49238704/673d2008-c93b-4e8f-9505-3cdcb2ba52cd" width="400">

## Getting Started

For a hands-on introduction to Graph2VR, we have created a video tutorial series that covers everything from basic navigation to advanced features. 
The tutorial is designed to help both beginners and experienced users get the most out of Graph2VR.

Check out the [Graph2VR Tutorial Series on YouTube](https://www.youtube.com/playlist?list=PLRQCsKSUyhNIdUzBNRTmE-_JmuiOEZbdH). 

For those new to Linked Data and SPARQL, we recommend Sir Tim Berners Lee's [Ted talk "The next Web"](https://www.ted.com/talks/tim_berners_lee_the_next_web) for some basics.

### Installation Instructions

The newest release can be found [here](https://github.com/molgenis/Graph2VR/releases).
It includes a Windows version (`Graph2VR_windows.zip`), and a standalone version for the Quest 2 or Quest 3 headset.

For the **Windows Version**: 
- Download the `Graph2VR_windows.zip` file from the [latest release](https://github.com/molgenis/Graph2VR/releases), unzip it, and execute the application.

For the **Quest 2/3 Standalone Version**:
- Ensure your Oculus Quest 2 or Quest 3 is in [developer mode](https://developer.oculus.com/documentation/native/android/mobile-device-setup/) to install the standalone version via SideQuest.
- We recommend [SideQuest](https://sidequestvr.com/download) for loading the application onto the Quest2/3 VR headset. When uploading the `.apk` file to the Quest headset, Graph2VR is in a category called "Unknown Sources applications" within the applications menu.

### Setting up a local Virtuoso Server using Docker 

To interact with your own dataset with Graph2VR a Virtuoso Server can be used as backend for Graph2VR.
A Virtuoso server can efficiently host large amounts of triples and serve as a backend for Graph2VR.
It can be used to load and expose Linked Data via a SPARQL Endpoint. 
We did not explicitly implement support for other servers, although DotNetRDF does offer the functions to connect to them.

A Virtuoso can for example be set up via Docker through the Docker GUI or by using the following commands:

docker pull tenforce/virtuoso
docker build -t virtuoso .
docker run -p 8890:8890 -p 1111:1111 --name virtuoso -d virtuoso

The SPARQL Endpoint can be reached at the selected port, in this case at [localhost:8890/sparql](http://localhost:8890/sparql). 
The web interface to configure the server can be found at [localhost:8890/conductor](http://localhost:8890/conductor). 
The web interface can also be used to upload small `.owl` or `.rdf` files. Larger files must be loaded via the Virtuoso bulk loader. 
When using the bulk loader, a "checkpoint" needs to be created; otherwise, the database might be gone once the Docker container is restarted. 

It could be necessary to adjust the Virtuoso server via the Virtuoso.ini file.
Usually, the "NumberOfBuffers" and "MaxDirtyBuffers" need to be increased, and the directories that the server is allowed to access "DirsAllowed" should be specified, when applicable. 
For large databases, it may also be necessary to extend the time limit for timeouts.

### Configuring Graph2VR

The file settings.txt file can be used to specify the SPARQL Endpoint(s) and the starting query.

For the standalone version, the settings file needs to be stored in the folder `sdcard/Android/data/com.Graph2VR.Graph2VR/files` on the headset.
Graph2VR creates this folder when a save state is created.

For the Windows version, the settings file can be put in the same folder as the `Graph2VR.exe` file.

In case of a missing settings file, internal default settings will be used.
The file can be placed next to the Graph2VR.exe for the Windows version. 

For the Quest 2/3 version, the settings.txt can be placed in the folder 'sdcard/Android/data/com.Graph2VR.Graph2VR/files'.
This path is created once a savestate is made.
You can find a sample settings.txt [here](https://github.com/molgenis/Graph2VR/releases/download/1.2.3/Settings.txt).

To access a specific SPARQL Endpoint, the `Virtuoso.ini` can be used to specify the server, the graph within the server and a starting query.
Note, that only CONSTRUCT queries can be used as initial queries, andd they need to be JSON-encoded.
While certain functionalities (such as BIND, MINUS, or subqueries) are not yet implemented as options within Graph2VR's GUI, these can still be used within the initial query.

## System Requirements

### Hardware Requirements
- **VR Headset**: Graph2VR is designed for Virtual Reality headsets, with dedicated support for the HTC Vive and Oculus Quest series (Quest 2 and Quest 3). 
Compatibility with other VR headsets has not been verified. However, at least two controllers are required to control the app.
We used OpenXR to set the controls, which makes the application less likely to run on other headsets as if it was built with SteamVR.
However, it supported us in building a standalone version of the application for the Quest 2/3.

### PC Specifications
  - **Memory**: 8GB+ RAM
  - **Graphics**: NVIDIA GTX 1060 / AMD Radeon RX 480 or greater - Better graphics cards are able to handle more nodes and improve the fonts' readability.
  - **Storage**: At least 4GB of free space is recommended.

### Software Requirements
- **Operating System**: Windows 10 or later for PC version; The standalone version is available for Oculus Quest 2/3.
- **Unity Engine**: For development, Unity version 2021.2.15f is recommended.

## License Information

**GNU Lesser General Public License v3.0 (LGPL-v3)**: The source code created by our team is licensed under LGPL-v3, which permits use, modification, and distribution, including for commercial purposes, as long as changes to our code are also shared under LGPL-v3. The LGPL-v3 does not apply to third-party components, which may have their own licenses.

The Software makes use of third-party libraries and software, including but not limited to DotNetRDF and Unity.
If you plan to use Graph2VR, ensure compliance with all relevant licenses, including obtaining a commercial Unity license if applicable.

The binaries of Graph2VR provided on this website were developed using an Educational License from Unity and are therefore intended only for non-commercial, educational use.

**As-Is**: This software is provided "as is" without warranty of any kind. 
Please check the issues pages for known bugs and missing features.

For more details, see the `LICENSE` file in this repository.
