using System;
using System.IO;
using System.Linq;
using System.Web;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using System.ServiceModel.Channels;
using System.Collections.Generic;
using Terminal.Gui;
using LDB;
using System.Diagnostics;
using ConfigurationManager = Terminal.Gui.ConfigurationManager;

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
		const LineStyle _line = LineStyle.Single;

		static void Main(string[] args)
		{
			Application.Init();

			Debug.WriteLine($"*** Themes({ConfigurationManager.Themes.Count}): {string.Join(',', ConfigurationManager.Themes.Keys)}");

			Application.AddTimeout(TimeSpan.FromMinutes(5), Refresh);

#if !TEST
			// token from command line?
			var token = args.Length == 1 || args.Length == 3 ? args [^1] : null;

			// appsetting
			var config = new ConfigurationBuilder().AddJsonFile("appSettings.json").Build();
			_token.TokenValue = token ?? config["token"] ?? "Null";

			if (!Guid.TryParse(_token.TokenValue, out Guid dummy))
			{
				MessageBox.ErrorQuery(80, 7, "Error", $"Invalid token:- {_token.TokenValue}", 0, "Quit");
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
			var themesMenu = new MenuItem[ConfigurationManager.Themes.Count];

			int i = 0;
			foreach (var theme in ConfigurationManager.Themes.Keys)
				themesMenu[i++] = new MenuItem(theme, "", () => SetColorScheme(theme));

			_menuBar = new MenuBar()
			{
				Menus = new MenuBarItem[]
			{
				new MenuBarItem("_File", new MenuItem[]
				{
					new MenuItem("_New", "", New),
					new MenuItem("_Quit", "", () => { if (Quit()) Application.Top.Running = false; })
				}),
				new MenuBarItem("_Options", new MenuItem[]
				{
					new MenuItem("_Refresh", "", GetBoard),
					new MenuBarItem("_Switch", new MenuItem[]
					{
						_switchMenu = new MenuItem("#swap#", "", Switch),
						_allDestinationMenu = new MenuItem("#alldest#", "", AllDestinations)
					}),
					new MenuBarItem("_Theme", themesMenu)
				}),
				new MenuBarItem("_Help", new MenuItem[]
				{
					new MenuItem("_About", "", About)
				})
			}};
			

			// main window
			_mainWindow = new Window() { Title = "Departures",  X = 0,	Y = Pos.Bottom(_menuBar), Width = Dim.Fill(), Height = Dim.Fill(), BorderStyle = _line };
			_mainWindow.KeyDown += (object sender, Key e) =>
			{
				if (e.KeyCode == Key.F5)
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
			var viewMessage = new FrameView() { Title = "Messages", X = 0, Y = Pos.Bottom(_displayDetails)+1, Width = Dim.Fill(), Height = Dim.Fill(), BorderStyle = _line };
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
					MessageBox.ErrorQuery(80, 7, "Error", $"Invalid 'from' station code:- {_fromStationCode}", 0, "Continue");
				else if(!_stationList.ContainsValue(_toStationCode) && _toStationCode != null)
					MessageBox.ErrorQuery(80, 7, "Error", $"Invalid 'to' station code:- {_toStationCode}", 0, "Continue");
				else
					Application.Invoke(GetBoard);
			}
			else
				Application.Invoke(New);

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

			var list = title.StartsWith("To ") ? _stationList.Keys.Prepend(all).ToList() : _stationList.Keys.ToList();

			var stationSearch = new ComboBox() { Width = Dim.Fill(), Height = Dim.Fill() };
			stationSearch.SetSource(list);
			stationSearch.OpenSelectedItem += (object sender, ListViewItemEventArgs e) => Application.RequestStop();

			var dialog = new Dialog() { Title = title, Width = Dim.Percent(40), Height = Dim.Percent(50) };
			dialog.Add(stationSearch);
			Application.Run(dialog);

			if (stationSearch.Text == all)
				return null;

			if (stationSearch.Text == string.Empty)
				return string.Empty;

			return _stationList[stationSearch.Text];
		}

		static void Switch()
		{
			if (_toStationCode == null) // cannot switch to from "All Destiations"
				return;

			(_toStationCode, _fromStationCode) = (_fromStationCode, _toStationCode);
			GetBoard();
		}

		static void AllDestinations()
		{
			_toStationCode = null;
			GetBoard();
		}

		static void About()
		{
			var ok = new Button() { Text = "Ok", IsDefault = true };

			ok.MouseClick += (object sender, MouseEventEventArgs e) => Application.RequestStop();

			var d = new Dialog() { Title = "About", Width = 36, Height = 8, BorderStyle = _line };
			d.AddButton(ok);

			d.Add(
				new Label() { Text = $"Live DepartureBoard {Assembly.GetEntryAssembly().GetName().Version}",  X = 0, Y = 1, Width = Dim.Fill(), TextAlignment = TextAlignment.Centered },
				new Label() { Text = $"{Environment.OSVersion.VersionString}", X = 0, Y = 2, Width = Dim.Fill(), TextAlignment = TextAlignment.Centered },
				new Label() { Text = $"DotNet {Environment.Version}", X = 0, Y = 3, Width = Dim.Fill(), TextAlignment = TextAlignment.Centered }
			);

			Application.Run(d);
		}

		static bool Quit()
		{
			return MessageBox.Query(50, 7, "Quit?", "Are you sure you want to quit?", 0, "Yes", "No") == 0;
		}

		static bool Refresh()
		{
			GetBoard();
			return true; // keep ticking
		}

		static void SetColorScheme(string name)
		{
			ConfigurationManager.Themes.Theme = name;
			ConfigurationManager.Apply();

			_mainWindow.SetNeedsDisplay();
		}

		static void GetBoard()
		{
			// Cannot use listview.Clear();
			_displayBoard.SetSource(Array.Empty<string>());
			_displayDetails.SetSource(Array.Empty<string>());
			_displayMessages.SetSource(Array.Empty<string>());
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
				MessageBox.ErrorQuery(78, 10, "Error", e.Message, 0, "Continue");
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
			_displayBoard.SelectedItem = 0;

			_rsids = board.trainServices.Select(x => x.serviceID).ToList();

			if (board.nrccMessages == null)
				return;

			_displayMessages.SetSource(board.nrccMessages.Select(x => HttpUtility.HtmlDecode( x.Value)).ToList());
		}

		static void GetDetialsBoard(object sender, ListViewItemEventArgs e)
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
			catch (Exception ex)
			{
				_displayMessages.SetSource(new List<string> { ex.Message });
				return;
			}
#endif
			_displayDetails.SetSource(details.subsequentCallingPoints[0].callingPoint.Select(x => $"{x.st} {x.locationName,-25}      {x.et,-10}").ToList());
			_displayDetails.SelectedItem = 0;
		}

#if TEST
		static ArrayOfCallingPoints1[] MakeCallingPoints(params string[] callingPoints)
		{
			var data = new ArrayOfCallingPoints1[]
			{
				new ArrayOfCallingPoints1  { callingPoint = new CallingPoint1[callingPoints.Length] }
			};

			var time = DateTime.Now;
			for (int i = 0; i < callingPoints.Length; i++, time = time.AddMinutes(4))
				data[0].callingPoint[i] = new CallingPoint1 { st = time.ToString("hh:mm"), locationName = callingPoints[i], et = "On time" };

			return data;
		}
#endif
	}
}
