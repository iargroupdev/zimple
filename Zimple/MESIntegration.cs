using System;
using System.Collections.Generic;
using libplctag.DataTypes;
using MES_HAI;
using MES_HAI.Entity; 

namespace Zimple
{
    public class MESIntegration
    {
        public Enums.ConnectionState Login(string station, string user, string password)
        {
            using (var traceability = new Traceability())
            {
                return traceability.Login(station, user, password);
            }
        }

        public (int ErrorCode, string ErrorDescription, string PartNumber)
            WorkOrder_GetActiveByStation(string station)
        {
            try
            {
                using (var traceability = new Traceability())
                {
                    var detail = traceability.WorkOrder_GetActiveByStation(station);
                    if (detail == null)
                        return (-1, "WorkOrderDetail is null", null);

                    return (detail.ErrorCode, detail.ErrorDescription, detail.WorkOrder?.PartNumber);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception in WorkOrder_GetActiveByStation: {ex.Message}");
                return (-1, ex.Message, null);
            }
        }

        public (int ErrorCode, string ErrorDescription)
            Serial_AssignToWorkOrder(string station, string serialNumber, int position)
        {
            try
            {
                using (var traceability = new Traceability())
                {
                    var result = traceability.Serial_AssignToWorkOrder(station, serialNumber, position);
                    if (result == null)
                        return (-1, "ErrorDetail is null");
                    return (result.ErrorCode, result.ErrorDescription);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception in Serial_AssignToWorkOrder: {ex.Message}");
                return (-1, ex.Message);
            }
        }

        public (int ErrorCode, string ErrorDescription)
            Serial_MoveIn(string station, string serialNumber, bool activateWorkOrder, int layer)
        {
            try
            {
                using (var traceability = new Traceability())
                {
                    var resp = traceability.Serial_MoveIn(station, serialNumber, activateWorkOrder, layer);
                    if (resp == null)
                        return (-1, "MoveInResponse is null");
                    return (resp.ErrorCode, resp.ErrorDescription);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception in Serial_MoveIn: {ex.Message}");
                return (-1, ex.Message);
            }
        }

        public (int ErrorCode, string ErrorDescription)
            Serial_SetAttribute(string station, string serialNumber, List<UnitInfo> unitInfo)
        {
            try
            {
                using (var traceability = new Traceability())
                {
                    var detail = traceability.Serial_SetAttribute(station, serialNumber, unitInfo);
                    if (detail == null)
                        return (-1, "ErrorDetail is null");
                    return (detail.ErrorCode, detail.ErrorDescription);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception in Serial_SetAttribute: {ex.Message}");
                return (-1, ex.Message);
            }
        }

        public (int ErrorCode, string ErrorDescription)
            Serial_MoveOut(string station, string serialNumber, Enums.Results result, int layer, bool checkMultiBoard)
        {
            try
            {
                using (var traceability = new Traceability())
                {
                    var detail = traceability.Serial_MoveOut(station, serialNumber, result, layer, checkMultiBoard);
                    if (detail == null)
                        return (-1, "ErrorDetail is null");
                    return (detail.ErrorCode, detail.ErrorDescription);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception in Serial_MoveOut: {ex.Message}");
                return (-1, ex.Message);
            }
        }

        public (int ErrorCode, string ErrorDescription)
            Serial_GetInformation(string station, string serialNumber)
        {
            try
            {
                using (var traceability = new Traceability())
                {
                    var detail = traceability.Serial_GetInformation(station, serialNumber);
                    if (detail == null)
                        return (-1, "ErrorDetail is null");
                    return (detail.ErrorCode, detail.ErrorDescription);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception in Serial_GetInformation: {ex.Message}");
                return (-1, ex.Message);
            }
        }

        public ErrorDetail Serial_MoveOutAndTestResults(
            string station, string serialNumber, Enums.Results result,
            string groupId, string groupVersion, Measure[] measures,
            int layer, bool checkMultiBoard)
        {
            try
            {
                using (var traceability = new Traceability())
                {
                    return traceability.Serial_MoveOutAndTestResults(
                        station, serialNumber, result,
                        groupId, groupVersion, measures, layer, checkMultiBoard
                    );
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception in Serial_MoveOutAndTestResults: {ex.Message}");
                return new ErrorDetail { ErrorCode = -1, ErrorDescription = ex.Message };
            }
        }

        public string GetVersion()
        {
            try
            {
                using (var traceability = new Traceability())
                {
                    return traceability.GetVersion();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception in GetVersion: {ex.Message}");
                return $"Error: {ex.Message}";
            }
        }

        public (Attributes attributes, string errorDescription)
            Serial_GetAttributeValues(string station, string serialNumber, string attributeKey)
        {
            try
            {
                using (var traceabilityInstance = new Traceability())
                {
                    var attributes = traceabilityInstance
                                        .Serial_GetAttributeValues(station, serialNumber, attributeKey);

                    if (attributes == null)
                    {
                        return (null, "Attributes object is null");
                    }
                    else
                    {
                        return (attributes, null);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception in Serial_GetAttributeValues: {ex.Message}");
                return (null, ex.Message);
            }
        }

        public MoveInResponse Serial_VerifyMergeNoMoveIn(string station, string childSerialNumber)
        {
            try
            {
                using (var traceability = new Traceability())
                {
                    return traceability.Serial_VerifyMergeNoMoveIn(station, childSerialNumber);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception in Serial_VerifyMergeNoMoveIn: {ex.Message}");
                return new MoveInResponse { ErrorCode = -1, ErrorDescription = ex.Message };
            }
        }

        public ErrorDetail Serial_Correlation(string station, List<string> children, string parentSerialNumber)
        {
            try
            {
                using (var traceability = new Traceability())
                {
                    return traceability.Serial_Correlation(station, children, parentSerialNumber);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception in Serial_Correlation: {ex.Message}");
                return new ErrorDetail { ErrorCode = -1, ErrorDescription = ex.Message };
            }
        }

        public WorkOrders WorkOrder_ListByStation(string station)
        {
            try
            {
                using (var traceability = new Traceability())
                {
                    return traceability.WorkOrder_ListByStation(station);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception in WorkOrder_ListByStation: {ex.Message}");
                return new WorkOrders { ErrorCode = -1, ErrorDescription = ex.Message };
            }
        }

        public ErrorDetail WorkOrder_Activate(string station, string workOrderName)
        {
            try
            {
                using (var traceability = new Traceability())
                {
                    return traceability.WorkOrder_Activate(station, workOrderName);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception in WorkOrder_Activate: {ex.Message}");
                return new ErrorDetail { ErrorCode = -1, ErrorDescription = ex.Message };
            }
        }

        public ErrorDetail Serial_ListNextByQuantity(string station, string objectName, int quantity)
        {
            try
            {
                using (var traceability = new Traceability())
                {
                    var sns = traceability.Serial_ListNextByQuantity(station, objectName, quantity);
                    return sns ?? new ErrorDetail { ErrorCode = -1, ErrorDescription = "No serials returned" };
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception in Serial_ListNextByQuantity: {ex.Message}");
                return new ErrorDetail { ErrorCode = -1, ErrorDescription = ex.Message };
            }
        }

        public ErrorDetail Serial_VerifyMerge(string station, string childSerialNumber)
        {
            // note: this called itself in your original—likely a typo,
            // so I’ve preserved it but you probably meant Traceability here:
            try
            {
                using (var traceability = new Traceability())
                {
                    return traceability.Serial_VerifyMerge(station, childSerialNumber);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception in Serial_VerifyMerge: {ex.Message}");
                return new ErrorDetail { ErrorCode = -1, ErrorDescription = ex.Message };
            }
        }

        public (int ErrorCode, string ErrorDescription, string NextOperation)
            Serial_GetPartTrackingAnnounceSerial(string station, string serialNumber)
        {
            try
            {
                using (var traceability = new Traceability())
                {
                    var pt = traceability.Serial_GetPartTrackingAnnnounceSerial(station, serialNumber);
                    if (pt == null)
                        return (-1, "PartTrackingAnnouncedSerialRepair is null", null);
                    return (pt.ErrorCode, pt.ErrorDescription, pt.NextOperation);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception in Serial_GetPartTrackingAnnounceSerial: {ex.Message}");
                return (-1, ex.Message, null);
            }
        }
    }
}