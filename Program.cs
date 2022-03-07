using System;
using System.IO;
using System.Linq;
using System.Web;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using System.ServiceModel.Channels;
using System.Collections.Generic;
using Terminal.Gui;
using NStack;
using LDB;

namespace DepartureBoard
{
	static class Program
	{
		static readonly LDBServiceSoapClient _client = new LDBServiceSoapClient(LDBServiceSoapClient.EndpointConfiguration.LDBServiceSoap12);
		static readonly AccessToken _token = new AccessToken();

		static Dictionary<string, string> _stationList = new Dictionary<string, string>();
		static Window _mainWindow;
		static MenuBar _menuBar;
		static List<string> _rsids;
		static ListView _displayBoard;
		static ListView _displayDetails;
		static ListView _displayMessages;
		static string _fromStationCode;
		static string _toStationCode;
		const string _allDestinations = "all destinations";
		static MenuItem _switchMenu;
		static MenuItem _allDestinationMenu;
		static ColorScheme _currentColourScheme = Colors.Base;
		static Border _border = new Border() { Effect3D = false, BorderStyle = BorderStyle.Single };

		static void Main(string[] args)
		{
			// Init Terminal.Gui
			Application.Init();
			Application.MainLoop.AddTimeout(TimeSpan.FromMinutes(5), Refresh);

#if !TEST
			// token from command line?
			var token = args.Length == 1 || args.Length == 3 ? args [^1] : null;

			// appsetting
			var config = new ConfigurationBuilder().AddJsonFile("appSettings.json").Build();
			_token.TokenValue = token ?? config["token"] ?? "Null";

			if (!Guid.TryParse(_token.TokenValue, out Guid dummy))
			{
				MessageBox.ErrorQuery(80, 7, "Error", $"Invalid token:- {_token.TokenValue}", 0, _border, "Quit");
				Application.Shutdown();
				return;
			}
#endif
			// station list
			var file = File.ReadAllLines("station_codes.csv");
			_stationList = file.ToDictionary(k => k.Split(',')[0], v => v.Split(',')[1]);

			// Web proxy https://github.com/dotnet/wcf/issues/3311
			if (_client.Endpoint.Binding is CustomBinding custom)
			{
				var binding = custom.Elements.Find<HttpsTransportBindingElement>();
				binding.UseDefaultWebProxy = true;
			}

			// menu bar
			_menuBar = new MenuBar(new MenuBarItem[]
			{
				new MenuBarItem ("_File", new MenuItem []
				{
					new MenuItem ("_New", "", New),
					new MenuItem ("_Quit", "", () => { if (Quit()) Application.Top.Running = false; })
				}),
				new MenuBarItem ("_Options", new MenuItem []
				{
					new MenuItem ("_Refresh", "", GetBoard),
					new MenuBarItem ("_Switch", new MenuItem[]
					{
						_switchMenu = new MenuItem ("#swap#", "", Switch),
						_allDestinationMenu = new MenuItem ("#alldest#", "", AllDestinations)
					}),
					new MenuBarItem ("Co_lour", new MenuItem[]
					{
						new MenuItem ("Standard", "", ColourStandard),
						new MenuItem ("Black/Yellow", "", () => SetColorScheme(Color.Black, Color.BrightYellow)),
						new MenuItem ("Black/Green", "", () => SetColorScheme(Color.Black, Color.BrightGreen)),
					})
				}),
				new MenuBarItem ("_Help", new MenuItem []
				{
					new MenuItem ("_About", "", About)
				})
			});

			// main window
			_mainWindow = new Window("Departures") { X = 0,	Y = Pos.Bottom(_menuBar), Width = Dim.Fill(), Height = Dim.Fill()};
			_mainWindow.KeyDown += (View.KeyEventEventArgs e) =>
			{
				if (e.KeyEvent.Key == Key.F5)
					GetBoard();

			};
			Application.Top.Add(_menuBar, _mainWindow);

			// Main board
			_displayBoard = new ListView() { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Percent(50) };
			_displayBoard.SelectedItemChanged += GetDetialsBoard;
			_mainWindow.Add(_displayBoard);

			// Details board
			_displayDetails = new ListView() { X = 0, Y = Pos.Bottom(_displayBoard), Width = Dim.Fill(), Height = Dim.Percent(25) };
			_mainWindow.Add(_displayDetails);

			// messages
			var viewMessage = new FrameView("Messages") { X = 0, Y = Pos.Bottom(_displayDetails)+1, Width = Dim.Fill(), Height = Dim.Fill() };
			_displayMessages = new ListView() { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill() };
			viewMessage.Add(_displayMessages);
			viewMessage.TabStop = false; // needs to happen after control added?!?
			_mainWindow.Add(viewMessage);

			// Run
			if (args.Length >= 2)
			{
				_fromStationCode = args[0];
				_toStationCode = args[1] == "ALL" ? null : args[1];

				if(!_stationList.ContainsValue(_fromStationCode))
					MessageBox.ErrorQuery(80, 7, "Error", $"Invalid 'from' station code:- {_fromStationCode}", 0, _border, "Continue");
				else if(!_stationList.ContainsValue(_toStationCode) && _toStationCode != null)
					MessageBox.ErrorQuery(80, 7, "Error", $"Invalid 'to' station code:- {_toStationCode}", 0, _border, "Continue");
				else
					Application.MainLoop.Invoke(GetBoard);
			}
			else
				Application.MainLoop.Invoke(New);

			Application.Run();
			Application.Shutdown();
		}

		static void New()
		{
			if ((_fromStationCode = SelectStation("From station")) == string.Empty)
				return;

			_displayBoard.SetFocus();

			if ((_toStationCode = SelectStation("To station")) == string.Empty)
				return;

			GetBoard();
		}

		static string SelectStation(string title)
		{
			string all = $"<{_allDestinations}>";

			var list = title.StartsWith("To ") ? _stationList.Keys.Prepend(all).Select(x => ustring.Make(x)).ToList() : _stationList.Keys.Select(x => ustring.Make(x)).ToList();

			var stationSearch = new ComboBox() { Width = Dim.Fill(), Height = Dim.Fill() };
			stationSearch.SetSource(list);
			stationSearch.OpenSelectedItem += (ListViewItemEventArgs _) => Application.RequestStop();

			var dialog = new Dialog() { Title = title, Width = Dim.Percent(40), Height = Dim.Percent(50), ColorScheme = _currentColourScheme };
			dialog.Border = _border;
			dialog.Add(stationSearch);
			Application.Run(dialog);

			if (stationSearch.Text == all)
				return null;

			if (stationSearch.Text == string.Empty)
				return string.Empty;

			return _stationList[stationSearch.Text.ToString()];
		}

		static void Switch()
		{
			if (_toStationCode == null) // cannot switch to from "All Destiations"
				return;

			var temp = _fromStationCode;
			_fromStationCode = _toStationCode;
			_toStationCode = temp;
			GetBoard();
		}

		private static void AllDestinations()
		{
			_toStationCode = null;
			GetBoard();
		}

		static void About()
		{
			var ok = new Button("Ok", is_default: true) { ColorScheme = _currentColourScheme };
			ok.Clicked += () => Application.RequestStop(); 

			var d = new Dialog("About", 36, 10, ok);
			d.Border = _border;
			d.Add(
				new Label($"Live DepartureBoard") { X = 0, Y = 1, Width = Dim.Fill(), TextAlignment = TextAlignment.Centered },
				new Label($"Version {Assembly.GetEntryAssembly().GetName().Version}") { X = 0, Y = 2, Width = Dim.Fill(), TextAlignment = TextAlignment.Centered },
				new Label($"{Environment.OSVersion.VersionString}") { X = 0, Y = 3, Width = Dim.Fill(), TextAlignment = TextAlignment.Centered }
			);

			Application.Run(d);
		}

		static bool Quit()
		{
			return MessageBox.Query(50, 7, "Quit?", "Are you sure you want to quit?", 0, _border, "Yes", "No") == 0;
		}

		static bool Refresh(MainLoop arg)
		{
			GetBoard();
			return true; // keep ticking
		}

		private static void ColourStandard()
		{
			_currentColourScheme = _menuBar.ColorScheme = _mainWindow.ColorScheme = Colors.Base;
			_mainWindow.SetNeedsDisplay();
			_menuBar.SetNeedsDisplay();
		}

		private static void SetColorScheme(Color back, Color fore)
		{
			var colour = Application.Driver.MakeAttribute(fore, back);
			_currentColourScheme = _menuBar.ColorScheme = _mainWindow.ColorScheme = new ColorScheme()
			{
				Normal = colour,
				HotNormal = colour,
				Focus = Application.Driver.MakeAttribute(fore, Color.DarkGray)
			};
			_mainWindow.SetNeedsDisplay();
			_menuBar.SetNeedsDisplay();
		}

		internal static void GetBoard()
		{
			// Cannot use listview.Clear();
			_displayBoard.SetSource(new List<string>());
			_displayDetails.SetSource(new List<string>());
			_displayMessages.SetSource(new List<string>());
			_displayBoard.SetFocus();
			_rsids?.Clear();

			StationBoard2 board;
#if TEST
			board = new StationBoard2
			{
				locationName = _stationList.First(x => x.Value == _fromStationCode).Key,
				filterType = FilterType.to,
				filterLocationName = _stationList.FirstOrDefault(x => x.Value == _toStationCode).Key,
				trainServices = new ServiceItem2[10],
				nrccMessages = new NRCCMessage[] { new NRCCMessage { Value = "Blackheath toilets out of order" } }
			};
			var time = DateTime.Now;
			for (int i = 0; i < board.trainServices.Length; i++)
			{
				board.trainServices[i] = new ServiceItem2 
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

			try
			{
				board = _client.GetDepartureBoard(_token, 10, _fromStationCode, _toStationCode, FilterType.to, 0, 120);
			}
			catch (Exception e)
			{
				MessageBox.ErrorQuery(78, 10, "Error", e.Message, 0, _border, "Continue");
				return;
			}

#endif
			_mainWindow.Title = $"{board.locationName} {board.filterType} {board.filterLocationName ?? _allDestinations}";

			_switchMenu.Title = $"{board.filterLocationName ?? _allDestinations} -> {board.locationName}";
			_allDestinationMenu.Title = $"{board.locationName} -> {_allDestinations}";

			bool switchMenuEnabled() { return board.filterLocationName != null; }

			_switchMenu.CanExecute = switchMenuEnabled;
			_allDestinationMenu.CanExecute = switchMenuEnabled;

			if (board.trainServices == null)
				return;

			_displayBoard.SetSource(board.trainServices.Select(x => $"{x.std} {x.destination[0].locationName,-25} {x.platform,-4} {x.etd,-10} {x.@operator}").ToList());
			_rsids = board.trainServices.Select(x => x.serviceID).ToList();

			if (board.nrccMessages == null)
				return;

			_displayMessages.SetSource(board.nrccMessages.Select(x => HttpUtility.HtmlDecode( x.Value)).ToList());
		}

		static void GetDetialsBoard(ListViewItemEventArgs args)
		{
			if (_rsids == null || _rsids.Count == 0) // fired before GetBoard called
				return;

			var serviceId = _rsids[_displayBoard.SelectedItem];

			if (serviceId == null)
				return;

			ServiceDetails details;
#if TEST
			details = new ServiceDetails
			{
				subsequentCallingPoints = MakeCallingPoints(
					"Lewisham",	"Lewisham2", "Lewisham3","Lewisham4","Lewisham5", // padding for test
					"St Johns",
					"New Cross",
					"London Bridge",
					"London Cannon Street")
			};
#else
			try
			{
				details = _client.GetServiceDetails(_token, serviceId);
			}
			catch (Exception e)
			{
				_displayMessages.SetSource(new List<string> { e.Message });
				return;
			}
#endif
			_displayDetails.SetSource(details.subsequentCallingPoints[0].callingPoint.Select(x => $"{x.st} {x.locationName,-25}      {x.et,-10}").ToList());
		}

#if TEST
		static ArrayOfCallingPoints1[] MakeCallingPoints(params string[] callingPoints)
		{
			var data = new ArrayOfCallingPoints1[]
			{
				new ArrayOfCallingPoints1  { callingPoint = new CallingPoint1[callingPoints.Length] }
			};

			var time = DateTime.Now;
			for (int i = 0; i < callingPoints.Length; i++)
			{
				data[0].callingPoint[i] = new CallingPoint1 { st = time.ToString("hh:mm"), locationName = callingPoints[i], et = "On time" };
				time = time.AddMinutes(4);
			}
			return data;
		}
#endif
	}
}
