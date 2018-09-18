﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using SmartHotel.Clients.Core.Extensions;
using SmartHotel.Clients.Core.Helpers;
using SmartHotel.Clients.Core.Models;
using SmartHotel.Clients.Core.Services.Authentication;
using SmartHotel.Clients.Core.Services.Request;

namespace SmartHotel.Clients.Core.Services.IoT
{
	public class RoomDevicesDataService : IRoomDevicesDataService
	{
		// TODO: Probably best to get this in AppSettings
		private readonly TimeSpan _sensorDataPollingInterval = TimeSpan.FromSeconds( 5 );
		private readonly string _roomDevicesApiEndpoint;
		private Timer _sensorDataPollingTimer;

		private readonly IRequestService _requestService;
		private readonly IAuthenticationService _authenticationService;

		private readonly ConcurrentDictionary<SensorDataType, DeviceSensorData>
			_currentSensorDataBySensorDataType = new ConcurrentDictionary<SensorDataType, DeviceSensorData>();

	    private string _thermostatDeviceId;
	    private string _lightDeviceId;
	    private readonly string _roomId;

        public RoomDevicesDataService( IRequestService requestService, IAuthenticationService authenticationService )
		{
			_requestService = requestService;
			_authenticationService = authenticationService;
			_roomDevicesApiEndpoint = AppSettings.RoomDevicesEndpoint;

			if ( !UseFakes )
			{
				if ( string.IsNullOrWhiteSpace( AppSettings.RoomId ) )
				{
					throw new Exception( $"{nameof( AppSettings )}.{nameof( AppSettings.RoomId )} must be specified." );
				}
			_roomId = AppSettings.RoomId;
		    _thermostatDeviceId = AppSettings.ThermostatDeviceId;
		    _lightDeviceId = AppSettings.LightDeviceId;

		}
		}

		public bool UseFakes => string.IsNullOrEmpty( _roomDevicesApiEndpoint );

        public async Task<RoomTemperature> GetRoomTemperatureAsync()
		{
			if ( UseFakes )
			{
				await Task.Delay( 1000 );

				return FakeRoomTemperature.Create();
			}

		    var storedValue = GetStoredSensorData<RoomTemperature>();
		    if (storedValue != null)
				{
		        return (RoomTemperature) storedValue;
				}

		    var roomData = await GetRoomSensorData(_authenticationService.AuthenticatedUser.Token, _roomId);
		    ProcessRoomData(roomData);

		    return (RoomTemperature) GetStoredSensorData<RoomTemperature>();
        }

	    public async Task<RoomAmbientLight> GetRoomAmbientLightAsync()
		{
			if ( UseFakes )
			{
				await Task.Delay( 1000 );

				return FakeRoomAmbientLight.Create();
			}

			var storedValue = GetStoredSensorData<RoomAmbientLight>();
			if ( storedValue != null )
			{
				return (RoomAmbientLight)storedValue;
			}

			var roomData = await GetRoomSensorData( _authenticationService.AuthenticatedUser.Token, _roomId );
			ProcessRoomData( roomData );

			return (RoomAmbientLight)GetStoredSensorData<RoomAmbientLight>();
		}


		private RoomSensorBase GetStoredSensorData<T>() where T : RoomSensorBase, new()
		{
			RoomSensorBase sensor = new T();
			if ( _currentSensorDataBySensorDataType.TryGetValue( sensor.SensorDataType,
				out DeviceSensorData sensorData ) )
			{
				var currentTemp = float.Parse( sensorData.SensorReading );
				var desiredTemp = float.Parse( sensorData.DesiredValue );

				if ( typeof( T ) == typeof( RoomTemperature ) )
					return new RoomTemperature( new SensorValue( currentTemp ), new SensorValue( desiredTemp ) );

				if ( typeof( T ) == typeof( RoomAmbientLight ) )
					return new RoomAmbientLight( new SensorValue( currentTemp * 100f ), new SensorValue( desiredTemp * 100f ) );
			}

			return null;
		}

		private void ProcessRoomData( IEnumerable<DeviceSensorData> data )
		{
			foreach ( var rawSensor in data )
			{
				if ( Enum.TryParse( rawSensor.SensorDataType, out SensorDataType dataType ) )
					_currentSensorDataBySensorDataType.AddOrUpdate( dataType, rawSensor, ( key, oldValue ) => rawSensor );
			}

		}

		private async Task<IEnumerable<DeviceSensorData>> GetRoomSensorData( string token, string roomId )
		{
			UriBuilder builder = new UriBuilder( _roomDevicesApiEndpoint );
			builder.AppendToPath( $"Devices/room/{roomId}" );
			var uri = builder.ToString();

			return await _requestService.GetAsync<IEnumerable<DeviceSensorData>>( uri, token );
		}


		public async Task UpdateDesiredAsync( float desiredTemperature, SensorDataType sensorDataType )
		{
		    UriBuilder builder = new UriBuilder( _roomDevicesApiEndpoint );
		    if ( _currentSensorDataBySensorDataType.TryGetValue( sensorDataType,
				out DeviceSensorData sensorData ) )
			{
				var sensorId = sensorData.SensorId;

			    builder.AppendToPath( "Devices" );
				var uri = builder.ToString();

                string methodName;
			    string deviceId;
                switch (sensorDataType)
                {
                    case SensorDataType.Temperature:
                        methodName = "SetDesiredTemperature";
                        deviceId = _thermostatDeviceId;
                        break;
                    case SensorDataType.Light:
                        methodName = "SetDesiredAmbientLight";
                        deviceId = _lightDeviceId;
                        break;
                    default:
                        throw new NotSupportedException(sensorDataType.ToString());
                }

                var request = new DeviceRequest
				{
					DeviceId = deviceId,
                    MethodName = methodName,
                    Value = desiredTemperature.ToString( CultureInfo.InvariantCulture )
				};

				await _requestService.PostAsync( uri, request, _authenticationService.AuthenticatedUser.Token );
			}
		}

		public void StartCheckingRoomSensorData()
		{
			if ( _sensorDataPollingTimer != null || UseFakes )
			{
				return;
			}

			_sensorDataPollingTimer = new Timer( _sensorDataPollingInterval, SensorDataPollingTimerTick );
			_sensorDataPollingTimer.Start();
		}

		public void StopCheckingRoomSensorData()
		{
			_sensorDataPollingTimer?.Stop();
			_sensorDataPollingTimer = null;
		}

		private async void SensorDataPollingTimerTick()
		{
			_sensorDataPollingTimer?.Stop();

			var roomData = await GetRoomSensorData( _authenticationService.AuthenticatedUser.Token, _roomId );
			ProcessRoomData( roomData );

			_sensorDataPollingTimer?.Start();
		}
	}
}