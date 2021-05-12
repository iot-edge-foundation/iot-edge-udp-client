# iot-edge-udp-client
UDP Client for Azure IoT Edge

## Introduction

This Azure IoT Edge module is a reference for handling UDP messages on Azure IoT Edge.

This repository has two parts:

1. Azure IoT Edge UDP Client module
2. UDP Test server (C# .Net Core) application 

## Limitation

This reference module shows to ingest regular UDP messages and sends them to the IoT Hub.

There is no actual handling (filtering, aggregation, etc.) of the incoming messages.

The current solution is also not optimized for speed. Due to the nature of UDP (no acknowledge of messages) there is a potential risk in missing messages being sent by the server. 

In a professional solution, receiving UDP messsages should be separated by (time consuming) message handling. A queue in between should optimize message handling without losing messages sent. 

The code is flexible, please check your own requirements.

## Azure IoT Edge routing outputs

This module exposes two outputs:

1. 'output1' for UDP messages
2. 'outputError' for logging messages

### UDP Message format

In pseudo, the following JSON message is exposed:

```
{
    DateTime timeStamp 
    string address
    int port
    string message
}
```

### Error Message format

In pseudo, the following JSON message is exposed:

```
{
    string logLevel
    string code
    string message
}
```

## Azure IoT Edge Client module properties

The module supports the following properties: 


```
{
    "clientListeningPort" : 11001,
    "minimalLogLevel" : "3"
}
```
### ClientListeningPort 

This is the port the client is listening on.

The desired property 'clientListeningPort' has a default value of 11001.

### ClientListeningPort 

This is the minimal log level used to send trace=0/debug=1/information=2/warning=3/error=4/critical=6/none=6 messages over the "outputError" output (lower means more messages).

The desired property 'minimalLogLevel' has a default value of Warning (3).

## Container Create Options

The following container create options must be configured to expose the client listening port:

```
{
    "ExposedPorts": { 
        "11001/udp":{}
    },
    "HostConfig": {
        "PortBindings":{
            "11001/udp":[ 
                {
                    "HostPort":"11001"
                }
            ]
        }
    }
}
```

## Public IP address

The client is listening on the public IP of the host, together with the configured port.

## Links

https://docs.microsoft.com/en-us/dotnet/api/system.net.sockets.udpclient.beginreceive?view=net-5.0

