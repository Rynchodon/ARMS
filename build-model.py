# This scripts builds all the .FBX files in the current directory into .mwm files using MwmBuilder.exe
# You will likely get Texture warnings while running this script.
# usage: build-model.py <Path to MwmBuilder.exe>

import os, os.path, shutil, subprocess, sys

buildIni = os.path.dirname(os.path.realpath(sys.argv[0])) + "\\build.ini"
startDir = os.getcwd()
input = "mwmBuilder\\Content"
output = input + "\\Output"

mwmBuilder = os.devnull

if len(sys.argv) >= 2:
	mwmBuilder = sys.argv[1]
	#print ("got mwmBuilder from argument")

if (os.path.exists(buildIni)):
	exec(open(buildIni).read())
	#print ("read build.ini file")

if not os.path.exists(mwmBuilder):
	print("ERROR: could not find mwmBuilder at " + mwmBuilder)
	sys.exit()

# test current directory contains fbx and xml files
bNoFBX = True
bNoXML = True
for file in os.listdir('.'):
	if file.endswith(".FBX"):
		bNoFBX = False
	else:
		if file.endswith(".xml"):
			bNoXML = False

if bNoFBX or bNoXML:
	print("ERROR: current directory does not contain .fbx and .xml files")
	sys.exit()


def createDir(l_dir):
	if not os.path.exists(l_dir):
		#print ("making: "+l_dir)
		os.makedirs(l_dir)

def copyWithExtension(l_from, l_to, l_ext):
	createDir(l_to)
	os.chdir(l_from)
	for file in os.listdir('.'):
		if file.endswith(l_ext):
			shutil.copy(file, l_to)


# set up directories for mwmBuilder
createDir(input)
createDir(output)
copyWithExtension(startDir, input, ".FBX")
copyWithExtension(startDir, input, ".xml")
copyWithExtension(startDir, input, ".hkt")

# run builder
#print("running mwmBuilder")
mwmBuilderProcess = subprocess.Popen([mwmBuilder, "/s:" + input, "/o:" + output])
mwmBuilderProcess.wait()
copyWithExtension(output, startDir, ".mwm")
