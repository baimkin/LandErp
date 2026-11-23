using System.Globalization;

namespace LandErp.ParserSpike.LocalCollection;

/// <summary>GeoJSON polygons, each containing an exterior ring followed by holes. Never a viewport.</summary>
public static class SearchAreaGeometry
{
    private static decimal Coordinate(decimal value) => decimal.Round(value, 7, MidpointRounding.AwayFromZero);

    public static string? Signature(GeoPoint[][][] polygons) => Signature(polygons, out _);

    private static string? Signature(GeoPoint[][][] polygons, out string? rejection)
    {
        rejection = "AREA_POLYGON_COUNT_INVALID";
        if (polygons.Length is 0 or > 64) return null;
        rejection = "AREA_POINT_LIMIT_EXCEEDED";
        if (polygons.Sum(p => p.Sum(r => r.Length)) > 12000) return null;
        List<string> areas = [];
        foreach (GeoPoint[][] polygon in polygons)
        {
            rejection = "AREA_RING_COUNT_INVALID";
            if (polygon.Length is 0 or > 128) return null;
            List<string> rings = [];
            foreach (GeoPoint[] source in polygon)
            {
                rejection = "AREA_RING_SIZE_INVALID";
                if (source.Length is < 4 or > 1024) return null;
                rejection = "AREA_COORDINATE_OUT_OF_RANGE";
                if (source.Any(p => p.Latitude is < -90 or > 90 || p.Longitude is < -180 or > 180)) return null;
                GeoPoint[] rounded = source.Select(p => new GeoPoint(Coordinate(p.Longitude), Coordinate(p.Latitude))).ToArray();
                rejection = "AREA_RING_NOT_CLOSED";
                if (rounded[0] != rounded[^1]) return null;
                List<GeoPoint> vertices = [];
                foreach (GeoPoint point in rounded[..^1]) if (vertices.Count == 0 || vertices[^1] != point) vertices.Add(point);
                if (vertices.Count > 1 && vertices[0] == vertices[^1]) vertices.RemoveAt(vertices.Count - 1);
                rejection = "AREA_RING_TOO_FEW_VERTICES";
                if (vertices.Count < 3) return null;
                rejection = "AREA_RING_REPEATED_VERTEX";
                if (vertices.Distinct().Count() != vertices.Count) return null;
                rejection = "AREA_RING_SELF_INTERSECTION";
                decimal area = 0;
                for (int i = 0; i < vertices.Count; i++)
                {
                    GeoPoint a = vertices[i], b = vertices[(i + 1) % vertices.Count];
                    area += a.Longitude * b.Latitude - b.Longitude * a.Latitude;
                    for (int j = i + 2; j < vertices.Count; j++)
                    {
                        if (i == 0 && j == vertices.Count - 1) continue;
                        if (Intersects(a, b, vertices[j], vertices[(j + 1) % vertices.Count])) return null;
                    }
                }
                rejection = "AREA_RING_ZERO_AREA";
                if (area == 0) return null;
                int start = Enumerable.Range(0, vertices.Count).MinBy(i => (vertices[i].Longitude, vertices[i].Latitude));
                string Walk(int direction) => string.Join(';', Enumerable.Range(0, vertices.Count).Select(i =>
                {
                    GeoPoint point = vertices[(start + direction * i + vertices.Count) % vertices.Count];
                    return point.Longitude.ToString("0.#######", CultureInfo.InvariantCulture) + "," + point.Latitude.ToString("0.#######", CultureInfo.InvariantCulture);
                }));
                string forward = Walk(1), reverse = Walk(-1);
                rings.Add(string.CompareOrdinal(forward, reverse) <= 0 ? forward : reverse);
            }
            // Hole ordering is incidental; exterior and holes cannot exchange roles.
            areas.Add(rings[0] + "|" + string.Join('|', rings.Skip(1).Order(StringComparer.Ordinal)));
        }
        rejection = null;
        return string.Join("||", areas.Order(StringComparer.Ordinal));
    }

    private static decimal Cross(GeoPoint a, GeoPoint b, GeoPoint c) =>
        (b.Longitude - a.Longitude) * (c.Latitude - a.Latitude) - (b.Latitude - a.Latitude) * (c.Longitude - a.Longitude);
    private static bool Intersects(GeoPoint a, GeoPoint b, GeoPoint c, GeoPoint d)
    {
        if (Math.Max(a.Longitude, b.Longitude) < Math.Min(c.Longitude, d.Longitude)
            || Math.Max(c.Longitude, d.Longitude) < Math.Min(a.Longitude, b.Longitude)
            || Math.Max(a.Latitude, b.Latitude) < Math.Min(c.Latitude, d.Latitude)
            || Math.Max(c.Latitude, d.Latitude) < Math.Min(a.Latitude, b.Latitude)) return false;
        return Math.Sign(Cross(a, b, c)) * Math.Sign(Cross(a, b, d)) <= 0
            && Math.Sign(Cross(c, d, a)) * Math.Sign(Cross(c, d, b)) <= 0;
    }

    public static bool Contains(GeoPoint[][][] polygons, ListingObservation item) =>
        item.Latitude.Parsed is decimal lat && lat is >= -90 and <= 90
        && item.Longitude.Parsed is decimal lon && lon is >= -180 and <= 180
        && Contains(polygons, new GeoPoint(lon, lat));
    public static bool Contains(GeoPoint[][][] polygons, GeoPoint point) =>
        point.Latitude is >= -90 and <= 90 && point.Longitude is >= -180 and <= 180
        && polygons.Any(polygon => AvitoMapData.Contains(polygon, point));

    public static GeoPoint[][][] Read(JsonResponseShape? shape) => Read(shape, out _);

    internal static GeoPoint[][][] Read(JsonResponseShape? shape, out string? rejection)
    {
        List<GeoPoint[][]> polygons = [];
        try
        {
            ReadShape(shape, polygons, 0);
            GeoPoint[][][] result = polygons.ToArray();
            return Signature(result, out rejection) is null ? [] : result;
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException or ArgumentException)
        {
            rejection = ex.Message switch
            {
                "AREA_GEOMETRY_UNAVAILABLE" => "AREA_GEOMETRY_UNAVAILABLE",
                "AREA_GEOMETRY_TYPE_UNSUPPORTED" => "AREA_GEOMETRY_TYPE_UNSUPPORTED",
                "AREA_INVALID_POINT" => "AREA_INVALID_POINT",
                _ => "AREA_GEOMETRY_VALUE_INVALID"
            };
            return [];
        }
    }

    private static void ReadShape(JsonResponseShape? shape, List<GeoPoint[][]> result, int depth)
    {
        if (depth > 8 || shape?.Fields is not { } fields) throw new FormatException("AREA_GEOMETRY_UNAVAILABLE");
        string? type = fields.GetValueOrDefault("type")?.Value as string;
        if (type == "Feature") { ReadShape(fields.GetValueOrDefault("geometry"), result, depth + 1); return; }
        if (type == "FeatureCollection")
        {
            foreach (JsonResponseShape feature in fields.GetValueOrDefault("features")?.Samples ?? []) ReadShape(feature, result, depth + 1);
            return;
        }
        JsonResponseShape? coordinates = fields.GetValueOrDefault("coordinates");
        if (type == "Polygon") result.Add(ReadPolygon(coordinates));
        else if (type == "MultiPolygon")
            foreach (JsonResponseShape polygon in coordinates?.Samples ?? []) result.Add(ReadPolygon(polygon));
        else throw new FormatException("AREA_GEOMETRY_TYPE_UNSUPPORTED");
    }

    private static GeoPoint[][] ReadPolygon(JsonResponseShape? coordinates) => (coordinates?.Samples ?? []).Select(ring =>
        (ring.Samples ?? []).Select(pair =>
        {
            if (pair.Samples is not { Length: 2 } values || values.Any(v => v.Type != "Number" || v.Value is null)) throw new FormatException("AREA_INVALID_POINT");
            return new GeoPoint(Convert.ToDecimal(values[0].Value, CultureInfo.InvariantCulture), Convert.ToDecimal(values[1].Value, CultureInfo.InvariantCulture));
        }).ToArray()).ToArray();
}
