using System;
using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Web;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using System.ServiceModel.Channels;
using System.Collections.Generic;
using Terminal.Gui;
using NStack;
using Mono.Terminal;
using LDB;

namespace DepartureBoard
{
	class DepartureBoard : Window
	{
		internal DepartureBoard(string title) : base(title){}
		public override bool ProcessKey(KeyEvent keyEvent)
		{
			if (keyEvent.Key == Key.F5)
				Program.GetBoard();

			base.ProcessKey(keyEvent);
			return true;
		}
	}

	class Program
	{
		static readonly LDBServiceSoapClient _client = new LDBServiceSoapClient(LDBServiceSoapClient.EndpointConfiguration.LDBServiceSoap12);
		static readonly AccessToken _token = new AccessToken();

		static Dictionary<string, string> _stationList = new Dictionary<string, string>();
		static DepartureBoard _mainWindow;
		static List<string> _rsids;
		static ListView _displayBoard;
		static ListView _displayDetails;
		static ListView _displayMessages;
		static string _fromStationCode;
		static string _toStationCode;

		#region Handlers
		static void New()
		{
			if ((_fromStationCode = SelectStation("From station")) == null)
				return;

			if ((_toStationCode = SelectStation("To station")) == null)
				return;

			GetBoard();
		}

		static string SelectStation(string title)
		{
			const string all = "<All destinations>";

			var list = title.StartsWith("To ") ? _stationList.Keys.Prepend(all).ToList() : _stationList.Keys.ToList();
			var stationSearch = new TextFieldAutoComplete(0, 0, 36, 7, list);
			stationSearch.Changed += (object sender, ustring s) => { Application.RequestStop(); };
			var dialog = new Dialog(title, 40, 12) { stationSearch };
			Application.Run(dialog);

			if (stationSearch.Text == "" || stationSearch.Text == all)
				return null;

			return _stationList[stationSearch.Text.ToString()];
		}

		static void About()
		{
			var d = new Dialog("About", 36, 10, new Button("Ok", is_default: true) { Clicked = () => { Application.RequestStop(); } })
			{
				new Label(new Rect(0, 1, 32, 1), $"Live DepartureBoard") { TextAlignment = TextAlignment.Centered },
				new Label(new Rect(0, 2, 32, 1), $"Version {Assembly.GetEntryAssembly().GetName().Version}") { TextAlignment = TextAlignment.Centered },
				new Label(new Rect(0, 3, 32, 1), $"{Environment.OSVersion.VersionString}") { TextAlignment = TextAlignment.Centered }
			};

			Application.Run(d);
		}

		static bool Quit()
		{
			return MessageBox.Query(50, 7, "Quit?", "Are you sure you want to quit?", "Yes", "No") == 0;
		}
		#endregion

		static void Main(string[] args)
		{
			// Init Terminal.Gui
			Application.Init();
			Application.MainLoop.AddTimeout(TimeSpan.FromMinutes(5), Refresh);
			var top = Application.Top;

#if !TEST
			// token from command line?
			var token = args.Length == 1 ? args [0] : null;

			// appsetting
			var config = new ConfigurationBuilder().AddJsonFile("appSettings.json").Build();
			_token.TokenValue =  token ?? config["token"] ?? "Null";

			if (!Guid.TryParse(_token.TokenValue, out Guid dummy))
			{
				MessageBox.ErrorQuery(80, 7, "Error", $"Invalid token:- {_token.TokenValue}", "Quit");
				return;
			}
#endif
			// station list
			var file = File.ReadAllLines(@"station_codes.csv");
			_stationList = file.ToDictionary(k => k.Split(',')[0], v => v.Split(',')[1]);


			// Web proxy https://github.com/dotnet/wcf/issues/3311
			if (_client.Endpoint.Binding is CustomBinding custom)
			{
				var binding = custom.Elements.Find<HttpsTransportBindingElement>();
				binding.UseDefaultWebProxy = true;
			}

			_mainWindow = new DepartureBoard("Departures") { X = 0,	Y = 1, Width = Dim.Fill(), Height = Dim.Fill()};

			// menu bar
			var menu = new MenuBar(new MenuBarItem[]
			{
				new MenuBarItem ("_File", new MenuItem []
				{
					new MenuItem ("_New", "", () => { New(); }),
					new MenuItem ("_Quit", "", () => { if (Quit()) top.Running = false; })
				}),				
				new MenuBarItem ("_Options", new MenuItem []
				{
					new MenuItem ("_Refresh", "", () => {  GetBoard(); }),					
					new MenuItem ("_Switch", "", () => {  Switch(); })

				}),
				new MenuBarItem ("_Help", new MenuItem []
				{
					new MenuItem ("_About", "", () => { About(); })
				})
			});

			top.Add(_mainWindow);
			top.Add(menu);

			// Main board
			_displayBoard = new ListView(new List<string>()) { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Percent(50) };
			_displayBoard.SelectedChanged += GetDetilsBoard;
			_mainWindow.Add(_displayBoard);

			// Details board
			_displayDetails = new ListView(new List<string>()) { X = 0, Y = 11, Width = Dim.Fill(), Height = Dim.Percent(50) };
			_mainWindow.Add(_displayDetails);

			// messages
			var viewMessage = new FrameView("Messages") { X = 0, Y = Pos.Bottom(_mainWindow)-8, Width = Dim.Fill(), Height = 5 };
			_displayMessages = new ListView();
			viewMessage.Add(_displayMessages);
			_mainWindow.Add(viewMessage);

			// Run
			if (args.Length >= 2)
			{
				_fromStationCode = args[0];
				_toStationCode = args[1];

				if(!_stationList.ContainsValue(_fromStationCode) || !_stationList.ContainsValue(_toStationCode))
				{
					MessageBox.ErrorQuery(80, 7, "Error", $"Invalid station code:- {_fromStationCode} {_toStationCode}");
					return;
				}
				else
					Application.MainLoop.Invoke(GetBoard);
			}
			else
				Application.MainLoop.Invoke(New);

			Application.Run();
		}

		static bool Refresh(MainLoop arg)
		{
			GetBoard();
			return true; // keep ticking
		}

		internal static void GetBoard()
		{
			_displayBoard.SetSource(new List<string>());
			_displayDetails.SetSource(new List<string>());
			_displayMessages.SetSource(new List<string>());
			_rsids?.Clear();

#if TEST
			var board = new StationBoard
			{
				locationName = _stationList.First(x => x.Value == _fromStationCode).Key,
				filterType = FilterType.to,
				filterLocationName = _stationList.First(x => x.Value == _toStationCode).Key,
				trainServices = new ServiceItem1[10],
				nrccMessages = new NRCCMessage[] { new NRCCMessage { Value = "Blackheath toilets out of order" } }
			};
			var time = DateTime.Now;
			for (int i = 0; i < board.trainServices.Length; i++)
			{
				board.trainServices[i] = new ServiceItem1 
				{ 
					std = time.ToString("hh:mm"), 
					destination = new ServiceLocation[] { new ServiceLocation { locationName = board.filterLocationName } }, 
					platform = "1", 
					etd = "On time",
					@operator = "Southeatern",
					serviceID = "1234"
				};
				time = time.AddMinutes(4);
			}
#else
			var task = _client.GetDepartureBoardAsync(_token, 10, _fromStationCode, _toStationCode, FilterType.to, 0, 120);
			try
			{
				task.Wait();
			}
			catch (Exception e)
			{
				MessageBox.ErrorQuery(78, 10, "Error", e.Message, "Continue");
				return;
			}

			var board = task.Result.GetStationBoardResult;
#endif
			_mainWindow.Title = $"{board.locationName} {board.filterType} {board.filterLocationName ?? "all destinations"}";

			if (board.trainServices == null)
				return;

			_displayBoard.SetSource(board.trainServices.Select(x => $"{x.std} {x.destination[0].locationName,-25} {x.platform,-4} {x.etd,-10} {x.@operator}").ToList());
			_rsids = board.trainServices.Select(x => x.serviceID).ToList();

			if (board.nrccMessages == null)
				return;

			_displayMessages.SetSource(board.nrccMessages.Select(x => HttpUtility.HtmlDecode(x.Value)).ToList());
		}

		static void GetDetilsBoard()
		{
			var serviceId = _rsids[_displayBoard.SelectedItem];

			if (serviceId == null)
				return;

			_displayDetails.SetSource(new List<string>());

#if TEST
			var details = new ServiceDetails
			{
				subsequentCallingPoints = MakeCallingPoints(
					"Lewisham",	"Lewisham2", "Lewisham3","Lewisham4","Lewisham5", // padding for test
					"St Johns",
					"New Cross",
					"London Bridge",
					"London Cannon Street")
			};
#else
			var task = _client.GetServiceDetailsAsync(_token, serviceId);
			try
			{
				task.Wait();
			}
			catch (Exception e)
			{
				_displayMessages.SetSource(new List<string> { e.Message });
				return;
			}

			var details = task.Result.GetServiceDetailsResult;
#endif
			_displayDetails.SetSource(details.subsequentCallingPoints[0].callingPoint.Select(x => $"{x.st} {x.locationName,-25}      {x.et,-10}").ToList());
		}

#if TEST
		static ArrayOfCallingPoints[] MakeCallingPoints(params string[] callingPoints)
		{
			var data = new ArrayOfCallingPoints[]
			{
				new ArrayOfCallingPoints  { callingPoint = new CallingPoint[callingPoints.Length] }
			};

			var time = DateTime.Now;
			for (int i = 0; i < callingPoints.Length; i++)
			{
				data[0].callingPoint[i] = new CallingPoint { st = time.ToString("hh:mm"), locationName = callingPoints[i], et = "On time" };
				time = time.AddMinutes(4);
			}
			return data;
		}
#endif

		static void Switch()
		{
			var temp = _fromStationCode;
			_fromStationCode = _toStationCode;
			_toStationCode = temp;
			GetBoard();
		}
	}
}
