using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.UI;
using System.Web.UI.WebControls;
using Microsoft.IdentityModel.Clients.ActiveDirectory;
using System.Threading.Tasks;
using System.Net.Http;
using System.Collections.Specialized;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net.Http.Headers;
using DarkSky.Services;
using NodaTime;
using System.Runtime.Serialization;

namespace AdventureWorksTravel
{
    public partial class _Default : Page
    {
        private const string DEFAULT_ML_SERVICE_LOCATION = "ussouthcentral";
        private const string BASE_ML_URI = "https://{0}.services.azureml.net/subscriptions/{1}/services/{2}/execute?api-version=2.0&details=true";
        
        private List<Airport> aiports = null;
        private ForecastResult forecast = null;
        private DelayPrediction prediction = null;

        // settings
        private string mlApiKey;
        private string mlWorkspaceId;
        private string mlServiceId;
        private string weatherApiKey;
        private string mlServiceLocation;
        private static DarkSkyService darkSky;

        protected void Page_Load(object sender, EventArgs e)
        {
            InitSettings();
            InitAirports();

            if (!IsPostBack)
            {
                txtDepartureDate.Text = DateTime.Now.AddDays(5).ToShortDateString();

                darkSky = new DarkSkyService(weatherApiKey);

                ddlOriginAirportCode.DataSource = aiports;
                ddlOriginAirportCode.DataTextField = "AirportCode";
                ddlOriginAirportCode.DataValueField = "AirportCode";
                ddlOriginAirportCode.DataBind();

                ddlDestAirportCode.DataSource = aiports;
                ddlDestAirportCode.DataTextField = "AirportCode";
                ddlDestAirportCode.DataValueField = "AirportCode";
                ddlDestAirportCode.DataBind();
                ddlDestAirportCode.SelectedIndex = 12;
            }
        }

        private void InitSettings()
        {
            mlApiKey = System.Web.Configuration.WebConfigurationManager.AppSettings["mlApiKey"];
            mlWorkspaceId = System.Web.Configuration.WebConfigurationManager.AppSettings["mlWorkspaceId"];
            mlServiceId = System.Web.Configuration.WebConfigurationManager.AppSettings["mlServiceId"];
            weatherApiKey = System.Web.Configuration.WebConfigurationManager.AppSettings["weatherApiKey"];
            mlServiceLocation = System.Web.Configuration.WebConfigurationManager.AppSettings["mlServiceLocation"];
        }

        private void InitAirports()
        {
            aiports = new List<Airport>()
            {
                new Airport() { AirportCode ="SEA", Latitude = 47.44900, Longitude = -122.30899 },
                new Airport() { AirportCode ="ABQ", Latitude = 35.04019, Longitude = -106.60900 },
                new Airport() { AirportCode ="ANC", Latitude = 61.17440, Longitude = -149.99600 },
                new Airport() { AirportCode ="ATL", Latitude = 33.63669, Longitude = -84.42810 },
                new Airport() { AirportCode ="AUS", Latitude = 30.19449, Longitude = -97.66989 },
                new Airport() { AirportCode ="CLE", Latitude = 41.41170, Longitude = -81.84980 },
                new Airport() { AirportCode ="DTW", Latitude = 42.21239, Longitude = -83.35340 },
                new Airport() { AirportCode ="JAX", Latitude = 30.49410, Longitude = -81.68789 },
                new Airport() { AirportCode ="MEM", Latitude = 35.04240, Longitude = -89.97669 },
                new Airport() { AirportCode ="MIA", Latitude = 25.79319, Longitude = -80.29060 },
                new Airport() { AirportCode ="ORD", Latitude = 41.97859, Longitude = -87.90480 },
                new Airport() { AirportCode ="PHX", Latitude = 33.43429, Longitude = -112.01200 },
                new Airport() { AirportCode ="SAN", Latitude = 32.73360, Longitude = -117.19000 },
                new Airport() { AirportCode ="SFO", Latitude = 37.61899, Longitude = -122.37500 },
                new Airport() { AirportCode ="SJC", Latitude = 37.36259, Longitude = -121.92900 },
                new Airport() { AirportCode ="SLC", Latitude = 40.78839, Longitude = -111.97799 },
                new Airport() { AirportCode ="STL", Latitude = 38.74869, Longitude = -90.37000 },
                new Airport() { AirportCode ="TPA", Latitude = 27.97550, Longitude = -82.53320 }
            };
        }

        protected async void btnPredictDelays_Click(object sender, EventArgs e)
        {
            var departureDate = DateTime.Parse(txtDepartureDate.Text);
            departureDate = departureDate.AddHours(double.Parse(txtDepartureHour.Text));

            var selectedAirport = aiports.FirstOrDefault(a => a.AirportCode == ddlOriginAirportCode.SelectedItem.Value);

            if (selectedAirport != null)
            {
                var query = new DepartureQuery()
                {
                    DepartureDate = departureDate,
                    DepartureDayOfWeek = ((int)departureDate.DayOfWeek) + 1, //Monday = 1
                    Carrier = txtCarrier.Text,
                    OriginAirportCode = selectedAirport.AirportCode,
                    OriginAirportLat = selectedAirport.Latitude,
                    OriginAirportLong = selectedAirport.Longitude,
                    DestAirportCode = ddlDestAirportCode.SelectedItem.Text
                };

                await GetWeatherForecast(query);

                if (forecast == null)
                    throw new Exception("Forecast request did not succeed. Check Settings for weatherApiKey.");

                PredictDelays(query, forecast).Wait();
            }

            UpdateStatusDisplay(prediction, forecast);
        }

        private void UpdateStatusDisplay(DelayPrediction prediction, ForecastResult forecast)
        {
            weatherForecast.ImageUrl = forecast.ForecastIconUrl;
            weatherForecast.ToolTip = forecast.Condition;

            if (String.IsNullOrWhiteSpace(mlApiKey))
            {
                lblPrediction.Text = "(not configured)";
                lblConfidence.Text = "(not configured)";
                return;
            }

            if (prediction == null)
                throw new Exception("Prediction did not succeed. Check the Settings for mlWorkspaceId, mlServiceId, and mlApiKey.");

            if (prediction.ExpectDelays)
            {
                lblPrediction.Text = "expect delays";
            }
            else
            {
                lblPrediction.Text = "no delays expected";
            }

            lblConfidence.Text = string.Format("{0:N2}", (prediction.Confidence * 100.0));
        }

        private async Task GetWeatherForecast(DepartureQuery departureQuery)
        {
            var departureDate = departureQuery.DepartureDate;
            forecast = null;

            try
            {
                var weatherPrediction = await darkSky.GetForecast(departureQuery.OriginAirportLat,
                    departureQuery.OriginAirportLong, new DarkSkyService.OptionalParameters
                    {
                        ExtendHourly = true,
                        DataBlocksToExclude = new List<ExclusionBlock> { ExclusionBlock.Flags,
                        ExclusionBlock.Alerts, ExclusionBlock.Minutely }
                    });
                if (weatherPrediction.Response.Hourly.Data != null && weatherPrediction.Response.Hourly.Data.Count > 0)
                {
                    var timeZone = DateTimeZoneProviders.Tzdb[weatherPrediction.Response.TimeZone];
                    var zonedDepartureDate = LocalDateTime.FromDateTime(departureDate)
                        .InZoneLeniently(timeZone);

                    forecast = (from f in weatherPrediction.Response.Hourly.Data
                                where f.DateTime == zonedDepartureDate.ToDateTimeOffset()
                                select new ForecastResult()
                                {
                                    WindSpeed = f.WindSpeed ?? 0,
                                    Precipitation = f.PrecipIntensity ?? 0,
                                    Pressure = f.Pressure ?? 0,
                                    ForecastIconUrl = GetImagePathFromIcon(f.Icon),
                                    Condition = f.Summary
                                }).FirstOrDefault();
                }

            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.TraceError("Failed retrieving weather forecast: " + ex.ToString());
            }
        }

        private string GetImagePathFromIcon<T>(T value)
            where T : struct, IConvertible
        {
            var defaultIconPath = Page.ResolveUrl("~/images/cloudy.svg");
            var enumType = typeof(T);
            var memInfo = enumType.GetMember(value.ToString());
            var attr = memInfo.FirstOrDefault()?.GetCustomAttributes(false).OfType<EnumMemberAttribute>().FirstOrDefault();
            return attr != null ? Page.ResolveUrl($"~/images/{attr.Value}.svg") : defaultIconPath;
        }

        private async Task PredictDelays(DepartureQuery query, ForecastResult forecast)
        {
            if (String.IsNullOrEmpty(mlApiKey))
            {
                return;
            }

            string fullMLUri = string.Format(BASE_ML_URI, !String.IsNullOrWhiteSpace(mlServiceLocation) ? mlServiceLocation : DEFAULT_ML_SERVICE_LOCATION, mlWorkspaceId, mlServiceId);
            var departureDate = DateTime.Parse(txtDepartureDate.Text);

            prediction = new DelayPrediction();

            try
            {
                using (var client = new HttpClient())
                {
                    var scoreRequest = new
                    {
                        Inputs = new Dictionary<string, StringTable>()
                        {
                            {
                                "input1",
                                new StringTable()
                                {
                                    ColumnNames = new string[]
                                    {
                                        "OriginAirportCode",
                                        "Month",
                                        "DayofMonth",
                                        "CRSDepHour",
                                        "DayOfWeek",
                                        "Carrier",
                                        "DestAirportCode",
                                        "WindSpeed",
                                        "SeaLevelPressure",
                                        "HourlyPrecip"
                                    },
                                    Values = new string[,]
                                    {
                                        {
                                            query.OriginAirportCode,
                                            query.DepartureDate.Month.ToString(),
                                            query.DepartureDate.Day.ToString(),
                                            query.DepartureDate.Hour.ToString(),
                                            query.DepartureDayOfWeek.ToString(),
                                            query.Carrier,
                                            query.DestAirportCode,
                                            forecast.WindSpeed.ToString(),
                                            forecast.Pressure.ToString(),
                                            forecast.Precipitation.ToString()
                                        }
                                    }
                                }
                            },
                        },
                        GlobalParameters = new Dictionary<string, string>()
                        {
                        }
                    };

                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", mlApiKey);
                    client.BaseAddress = new Uri(fullMLUri);
                    HttpResponseMessage response = await client.PostAsJsonAsync("", scoreRequest).ConfigureAwait(false);

                    if (response.IsSuccessStatusCode)
                    {
                        string result = await response.Content.ReadAsStringAsync();
                        JObject jsonObj = JObject.Parse(result);

                        string prediction = jsonObj["Results"]["output1"]["value"]["Values"][0][10].ToString();
                        string confidence = jsonObj["Results"]["output1"]["value"]["Values"][0][11].ToString();

                        if (prediction.Equals("1"))
                        {
                            this.prediction.ExpectDelays = true;
                            this.prediction.Confidence = double.Parse(confidence);
                        }
                        else if (prediction.Equals("0"))
                        {
                            this.prediction.ExpectDelays = false;
                            this.prediction.Confidence = double.Parse(confidence);
                        }
                        else
                        {
                            this.prediction = null;
                        }

                    }
                    else
                    {
                        prediction = null;

                        Trace.Write(string.Format("The request failed with status code: {0}", response.StatusCode));

                        // Print the headers - they include the request ID and the timestamp, which are useful for debugging the failure
                        Trace.Write(response.Headers.ToString());

                        string responseContent = await response.Content.ReadAsStringAsync();
                        Trace.Write(responseContent);
                    }
                }
            }
            catch (Exception ex)
            {
                prediction = null;
                System.Diagnostics.Trace.TraceError("Failed retrieving delay prediction: " + ex.ToString());
                throw;
            }
        }
    }

    #region Data Structures

    public class StringTable
    {
        public string[] ColumnNames { get; set; }
        public string[,] Values { get; set; }
    }

    public class ForecastResult
    {
        public double WindSpeed;
        public double Precipitation;
        public double Pressure;
        public string ForecastIconUrl;
        public string Condition;
    }

    public class DelayPrediction
    {
        public bool ExpectDelays;
        public double Confidence;
    }

    public class DepartureQuery
    {
        public string OriginAirportCode;
        public double OriginAirportLat;
        public double OriginAirportLong;
        public string DestAirportCode;
        public DateTime DepartureDate;
        public int DepartureDayOfWeek;
        public string Carrier;
    }

    public class Airport
    {
        public string AirportCode { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
    }

    #endregion
}