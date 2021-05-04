dotnet publish
scp -r ./bin/Debug/netcoreapp3.1/publish pi@raspberrypi.lan:/home/pi/smartinverter
